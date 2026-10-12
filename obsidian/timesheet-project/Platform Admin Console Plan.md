# Platform Admin Console Plan

> Drafted 12 October 2026 from a code audit of `develop` at `6bd3f83`; approved the same day with every recommended decision. Builds on [[Platform Organization Management Plan]], which is fully implemented.

## Implementation Progress

- **Phase A** — implemented on `fix/platform-console-hardening`:
  - A1: `POST /api/v1/platform/organizations` creates the organization and its `organization.created` audit row in one transaction, issues no onboarding key, and sends the activation email. The SysAdmin page uses it.
  - A2: `PlatformOrganizationsHttpGuardTests` covers anonymous, forged, wrong-role, wrong-tenant, non-admin account, forced-change, stale session version, and deactivated-membership tokens. `tests/platform/Smoke.ps1` checks that Gateway rejects anonymous and forged calls to platform routes.
  - A3: Gateway `platform` rate-limit policy (120 requests per minute per IP) on `platform-admin-api`.
  - A4: `POST /api/v1/auth/login` binds `OrganizationLoginRequest` and never accepts `tenantId`. The phase 2–5 smoke scripts log in by organization code; phase 2 asserts that a tenant-ID login is rejected.
  - Decision 1 (F10) is enforced in tenant creation, both invitation paths, invitation acceptance, and Platform Admin bootstrap.
- **Phase B** — implemented on `feature/platform-audit-and-overview`:
  - B1: `GET /api/v1/platform/audit` (filters: organization, actor email, action, UTC date range; newest first; paged) and the `/sys-admin/audit` page. Entries show the action, actor, organization, changed fields, and reason. Values render as text. The audit log is reachable from an organization's detail page, pre-filtered to that organization.
  - B2: `GET /api/v1/platform/overview` and the `/sys-admin` landing page: organization totals by status, organizations without an active administrator, the newest organizations, and recent activity. Sys-admin sign-in, the Shell's "System admin" link, and the home card now land here.
  - B3: `GET /api/v1/platform/organizations/{tenantId}` and `/sys-admin/organizations/{tenantId}`: code, URL name, status, created date, active/pending member counts, administrators, and the organization's history. Only counts and administrators are exposed, per decision 3.
  - B4: `platform_admin.signed_in` is recorded for every new Platform Admin session on any sign-in route; `platform_admin.sign_in_failed` is recorded for rejected `/sys-admin/login` attempts, storing the email only when well-formed.
  - The Platform Admin guard moved into `PlatformActorResolver` and `PlatformControllerBase`, shared by `PlatformOrganizationsController` and the new `PlatformConsoleController`.
- **Phase C** — implemented on `feature/platform-admin-roster`:
  - C1: `GET/POST /api/v1/platform/admins` and `PUT /api/v1/platform/admins/status`, plus the `/sys-admin/admins` page. Inviting creates a dedicated platform account (refused for any email in a customer organization) that goes through the normal activation flow. Inviting a pending admin again re-sends activation. Deactivation sets the new `MembershipStatus.Deactivated`, bumps the session version, revokes refresh sessions, and publishes the revocation. Reactivation restores `Active`, or `PendingActivation` (with a new activation email) if no password was ever set. An admin cannot deactivate themselves (`CANNOT_CHANGE_SELF`), so at least one active admin always remains; the service also refuses to deactivate the last active one (`LAST_ADMINISTRATOR`). `BOOTSTRAP_PLATFORM_ADMIN_EMAIL` stays the recovery path: point it at a new email. It never revives a deactivated membership.
  - C2: `POST /api/v1/platform/organizations/{tenantId}/administrators/demote` (role becomes `Employee`) and `.../remove` (membership becomes `Deactivated`), with Make employee / Remove actions on the organization detail page. Both refuse to leave an organization without an active administrator (pending admins do not count). The account's sessions end in every organization, because the session version is per account.
  - Audit actions: `platform_admin.invited`, `platform_admin.activation_resent`, `platform_admin.deactivated`, `platform_admin.reactivated`, `administrator.demoted`, `administrator.removed`. The labels live in one shared list (`composables/platformAuditActions.ts`) used by the audit list and its filter.
  - Known limit: a removed member cannot be invited back yet (the invitation is refused as a conflict), because no member reactivation route exists.
- **Phase D** — implemented on `feature/platform-admin-security`:
  - D1: `GET/PUT /api/v1/platform/security/password-policy` and the `/sys-admin/security` page edit the platform tenant's policy. They reuse the tenant-policy rules: a stricter policy forces every Platform Admin to change password and ends their sessions. The change is audited (`platform.password_policy_updated`) with the old and new settings. Invalid values are rejected before the tenant is touched.
  - D2: TOTP (RFC 6238, SHA-1, 30 s, 6 digits, ±1 step, no replay of an accepted step) is required on `POST /api/v1/auth/platform-admin/login`. `StartSessionAsync` refuses any Platform Admin session without a verified second factor, so `/auth/sign-in` and `/auth/login` answer `MFAREQUIRED` for those accounts. First sign-in offers an enrollment secret (kept for 15 minutes so a mistyped code does not force a rescan); the first valid code enables it and returns ten single-use recovery codes, stored only as SHA-256 hashes and spent with a conditional update. Five wrong second factors lock the account for 15 minutes; they are counted separately from password failures, because a correct password resets that counter. Secrets are encrypted with AES-256-GCM under `MFA_ENCRYPTION_KEY`; a secret that cannot be decrypted answers 503 and does not count toward the lockout. Another Platform Admin resets MFA from the roster (`POST /api/v1/platform/admins/mfa-reset`, never your own), which clears the authenticator and recovery codes and ends sessions. Audit actions: `platform_admin.mfa_enrolled`, `platform_admin.recovery_code_used`, `platform_admin.mfa_reset`; a wrong second factor is recorded as `platform_admin.sign_in_failed`. Migration `AddPlatformAdminMfa` adds the account columns and `mfa_recovery_codes`.
  - Existing Platform Admins enroll at their next sign-in. First enrollment is trust-on-first-use: whoever holds the password before enrollment can enroll, which is why enrollment and resets are audited.

## Permission Audit

### What already holds

- **Two independent guards on platform routes.** Gateway route `platform-admin-api` (`/api/v1/platform/**`) requires the `PlatformAdmin` role. Identity's `PlatformOrganizationsController.PlatformActorAsync` then re-validates on every request: role `PlatformAdmin`, token tenant equal to the platform tenant, no forced password change, account `IsPlatformAdmin` with a matching session version, and an active `PlatformAdmin` membership. The service layer checks again in `EnsurePlatformAdminAsync`.
- **Tenant isolation.** Every tenant-scoped service takes the tenant from the validated token; Gateway strips caller-supplied `X-Tenant-Id`/`X-User-Id`/`X-Role`. A Platform Admin token is scoped to the platform tenant and cannot reach customer data through tenant routes.
- **Role boundaries (fixed in `fix/admin-role-boundaries`).** Timesheet and Report grant admin rights only to `TenantAdmin`; the Shell shows Platform Admins only Profile and Organizations and blocks direct `/remote/<id>` visits.
- **Protected platform tenant.** It is excluded from the organization list and cannot be edited or suspended (`PROTECTED_TENANT`).
- **Suspension is enforced.** Login, refresh, activation, and reset reject suspended tenants; token validation rejects them; suspension publishes a tenant revocation.
- **Dedicated sign-in.** `/sys-admin/login` issues a session only for an active `PlatformAdmin` membership.

### Gaps

| # | Severity | Finding | Evidence |
|---|---|---|---|
| F1 | High | Sys admins create organizations through the anonymous public signup endpoint `POST /api/v1/tenants`. The creation has no actor, no platform authorization, and no audit record, and the response carries the one-time onboarding key into the sys admin's browser (the page discards it). | `sys-admin/organizations.vue` `createOrganization`; Gateway `OnboardingController.CreateTenant` |
| F2 | High | There is exactly one Platform Admin, bootstrapped from `BOOTSTRAP_PLATFORM_ADMIN_EMAIL`. No API or UI exists to add, disable, or replace Platform Admins; losing that account requires a config change and redeploy. | `PlatformAdminBootstrap`; no platform-admin routes |
| F3 | High | The most privileged role has password-only authentication. | `AuthenticationService` |
| F4 | Medium | `platform_organization_audit` is written but has no read API or UI, so administrative history is invisible. Organization creation (F1) and Platform Admin sign-ins are not audited at all. | `PlatformOrganizationService.AddAudit`; no reader |
| F5 | Medium | The platform tenant's password policy cannot be changed: the policy routes require `TenantAdmin`, and the platform tenant has none. Platform Admins are stuck on defaults. | `IdentityController.UpdatePasswordPolicy`, `ProfileController.PasswordPolicy` |
| F6 | Medium | Tenant administrators can be invited and re-sent activation, but not removed or demoted. | `PlatformOrganizationService` (no revoke operation) |
| F7 | Medium | The controller-level guard (`PlatformActorAsync`: wrong role, wrong tenant, forced password change, stale session version) has no HTTP-level tests, and no smoke script calls `/api/v1/platform/**`. | `tests/Aiyara.Phase1.Tests/PlatformOrganizationManagementTests.cs` covers the service only |
| F8 | Low | Platform routes have no Gateway rate-limit policy. | Gateway `appsettings.json` `platform-admin-api` |
| F9 | Low | `POST /api/v1/auth/login` still accepts a caller-supplied `tenantId`. An active membership is still required, so this is not an escalation, but it bypasses the organization-code/email flows. | `AuthenticationService.LoginAsync` |
| F10 | Decision | A Platform Admin's email can also hold a customer-tenant membership. With email-only sign-in that tenant appears in the organization picker next to the platform tenant. | `SignInAsync`, `InviteAdministratorAsync` |

## Goal

Turn `/sys-admin` into a complete, audited console: every platform action goes through platform-guarded, audited routes, Platform Admins can be managed without redeploying, and administrative history is visible.

## Delivery Plan

Each phase is one Git Flow branch, validated per `AGENTS.md` before merging to `develop`.

### Phase A — Close the existing gaps (`fix/platform-console-hardening`)

- **A1 (F1).** Add `POST /api/v1/platform/organizations`, guarded like the other platform routes. It reuses `OnboardingService.CreateTenantAsync`, records an `organization.created` audit entry with the actor, and returns the organization code without the onboarding key. Switch the SysAdmin page to it. Public self-signup at `/signup` stays as it is.
- **A2 (F7).** Add HTTP tests for `PlatformActorAsync`: Tenant Admin, Employee, forced password change, stale session version, inactive membership, and a token from another tenant, each expecting 401. Add a `tests/platform` smoke step that calls `GET /api/v1/platform/organizations` without a token and with a tenant token.
- **A3 (F8).** Attach a rate-limit policy to `platform-admin-api`.
- **A4 (F9).** Decide whether to drop `tenantId` from the public `LoginRequest`. The smoke scripts that send it must move to `organizationCode`.

### Phase B — Visibility (`feature/platform-audit-and-overview`)

- **B1 (F4).** `GET /api/v1/platform/audit` returns audit entries filtered by organization, actor, action, and date range, with paging. Add a `/sys-admin/audit` page and an organization-scoped history panel. Show reasons and changed fields; never credentials or codes.
- **B2.** Add a `/sys-admin` overview: active and suspended counts, organizations with pending administrator activation, and the newest organizations. All values are counts or metadata; tenant timesheet content stays out of reach.
- **B3.** Add an organization detail page with name, slug, code, status history, administrators with activation state, member count, and created date.
- **B4.** Audit Platform Admin sign-in success and failure.

### Phase C — People (`feature/platform-admin-roster`)

- **C1 (F2).** Platform Admin roster: invite a Platform Admin (same activation flow), deactivate, and reactivate. Refuse to deactivate the last active Platform Admin or yourself. Deactivation bumps the session version so live sessions end. Every change is audited. Keep the env bootstrap as the recovery path.
- **C2 (F6).** Remove or demote a tenant administrator, refusing to leave an organization without an active administrator. Audit each change.

### Phase D — Security (`feature/platform-admin-security`)

- **D1 (F5).** Platform security settings: let Platform Admins edit the platform tenant's password policy through a platform route. Customer tenants' policies stay with their Tenant Admins.
- **D2 (F3).** TOTP multi-factor authentication, required for `PlatformAdmin` sign-in: enrollment, recovery codes, and reset by another Platform Admin. Audit enrollment and resets.

### SysAdmin information architecture

`/sys-admin` gets its own sub-navigation: Overview, Organizations (list → detail), Audit log, Platform admins, Security. Every page keeps the existing client-side role check, but authority stays on the server.

## Decisions (agreed 12 October 2026)

1. **F10:** may a Platform Admin also belong to a customer organization? Recommended: no. Reject inviting a Platform Admin's email into a customer tenant, and reject promoting a customer member to Platform Admin. This keeps the console identity separate from customer data.
2. Is MFA (D2) in scope now, or after Phases A–C? Recommended: right after A–C, before any production rollout.
3. Should sys admins see customer member lists (names and emails), or only counts and administrators? Recommended: counts and administrators only.
4. Organization deletion and data export are left out. Suspension stays the only lifecycle action until a retention policy is agreed.

## Acceptance Criteria

- Every SysAdmin mutation goes through a `/api/v1/platform/**` route that is platform-guarded and audited; no SysAdmin page calls an anonymous route.
- The audit log shows who did what, to which organization, when, and why, without secrets.
- More than one Platform Admin can exist; the last active one cannot be removed; removing one ends their sessions.
- Tenant Admins, Employees, and tokens from any other tenant receive 401/403 from every platform route, proven by HTTP tests and smoke.
- Platform Admin sign-in requires a second factor once D2 ships.

## Related

- [[Platform Organization Management Plan]]
- [[Security And Tenant Model]]
- [[Project Status]]
- [[Timesheet Project Index]]
