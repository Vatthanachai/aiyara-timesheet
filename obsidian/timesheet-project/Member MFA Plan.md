# Member MFA Plan

> Drafted 22 October 2026 from item 7 of [[Product Backlog]] and a code audit of `develop` at `7199e5e`. Phase A (Identity API) is implemented on `feature/member-mfa`; phase B (UI) follows on the same branch. Covers how a Tenant Admin requires a second factor of the organization's members, and how members enroll, answer it, and recover.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| TOTP | `Totp` (RFC 6238, SHA-1, six digits, 30 s steps, one step of drift), `Account.MfaLastUsedStep` blocks a replayed code | Platform sign-in page (`sys-admin/login.vue`) |
| Secret storage | `AesGcmMfaSecretProtector` (key from `Mfa:EncryptionKey`); `Account.MfaSecretProtected`, `MfaPendingSecretProtected` (15-minute pending secret) | — |
| Recovery codes | `mfa_recovery_codes`: ten `XXXXX-XXXXX` codes, stored as hashes, spent with a conditional update so two sign-ins cannot both use one | Shown once after enrollment |
| Brute force | Five wrong second factors lock the account for 15 minutes (`MfaFailedCount`, `LockedUntilUtc`, shared with the password lockout); the Redis attempt limiter allows ten sign-in calls a minute per email | — |
| Platform Admin sign-in | `LoginPlatformAdminAsync`: no authenticator → an enrollment secret and no session; the first valid code confirms it and returns recovery codes | Same page |
| Admin reset | `PlatformAdminRosterService.ResetMfaAsync`: another Platform Admin clears the factor, ends sessions, audits with a reason | Admins page |
| Members | `StartSessionAsync` refuses a Platform Admin session without a second factor; members have none | — |

Accounts are global (one per email), and one account can be a member of several organizations; the authenticator therefore belongs to the account, while the requirement belongs to the organization.

## Decisions

1. **The requirement is `tenants.RequireMfa`** (`bool`, default false), set by a Tenant Admin with `PUT /api/v1/organization/mfa` `{ required, reason? }` and audited (`organization.mfa_requirement_changed`, before and after, reason). It applies to every member of the organization; Platform Admins keep their own mandatory flow and are never subject to it.
2. **Enforced at the next sign-in, no grace period.** A grace period needs a deadline column, a reminder email, and a scheduled job, and leaves some members unprotected for days on the admin's own say-so. Enforcing at the next sign-in needs none of that: turning the requirement on ends the sessions of every active member who has no authenticator (`SessionVersion` bump, refresh sessions revoked, revocation published to Gateway), and they enroll when they sign back in. Members who already have an authenticator keep their sessions, which already passed a second factor. `RefreshAsync` also refuses an un-enrolled member when the organization requires MFA, so no stale session can slip through.
3. **The admin must be enrolled first.** Turning the requirement on is refused (`409 MFA_NOT_ENROLLED`) unless the acting admin has an authenticator, so the admin has seen the flow and cannot lock themselves out. Turning it off needs nothing and unenrolls nobody.
4. **Enrollment happens inside the first sign-in, before any session exists.** A member who must enroll gets no token at all, so there is no half-privileged session to guard. `auth/login` and `auth/sign-in` answer `401 MFA_ENROLLMENT_REQUIRED`; the client calls `POST auth/mfa/enrollment` (email and password) for the secret and the `otpauth://` URI, then repeats the sign-in with `totpCode`. A right code issues the session and the response carries `recoveryCodes` (shown once). The pending secret and its 15-minute life are the Platform Admin's.
5. **An enrolled account is always asked, in every organization.** The account is global, so a member who enrolled for organization A, or by choice, answers `totpCode` or `recoveryCode` at organization B too (`401 MFAREQUIRED` without one, `401 MFAINVALID` when wrong). Only a requiring organization forces enrollment. Choosing an organization in `auth/sign-in` for a multi-organization account needs the password only; the second factor comes with the login that follows.
6. **Members may opt in voluntarily**, from a signed-in page: `POST mfa/enrollment` then `POST mfa/enrollment/confirm`. They may opt out with a code (`POST mfa/disable`) unless any active organization of theirs requires MFA (`409 MFA_REQUIRED_BY_ORGANIZATION`).
7. **Recovery codes**: same ten codes, hashes only, single use by a conditional update. A member regenerates them with `POST mfa/recovery-codes`, which needs a current authenticator code (a recovery code is not enough), so a stolen session alone cannot mint codes. Regenerating replaces all ten.
8. **Admin reset** (`POST members/{accountId}/mfa-reset` `{ reason? }`, Tenant Admin, `204`): clears the secret, pending secret, replay step, failure count, and recovery codes, ends the member's sessions, and audits `member.mfa_reset` with the reason in the admin's organization. It needs the admin's own authorization as every member action does (TenantAdmin token, active admin, current session version, password not forced or expired). Admins cannot reset themselves (`409 CANNOT_CHANGE_SELF`), so a stolen admin session cannot remove the admin's own factor; another admin does it. A member of another organization is `404`. The reset applies to the whole account (see Known Limits).
9. **Lockout and rate limits are the Platform Admin's.** Five wrong codes in a row (counted for authenticator and recovery codes alike, at sign-in, enrollment, regeneration, and opt-out) lock the account 15 minutes (`member.mfa_locked` is audited in the organization); the signed-in endpoints answer `429 RATELIMITED` while locked, so a stolen session cannot guess codes either. A right code resets the counter. The password attempt limiter and the `auth` rate limit still apply to every call.
10. **Audit** (`platform_organization_audit`, in the member's organization, which Tenant Admins will see once backlog item 8 exposes it): `organization.mfa_requirement_changed`, `member.mfa_enrolled`, `member.mfa_recovery_code_used`, `member.mfa_recovery_codes_regenerated`, `member.mfa_disabled`, `member.mfa_reset`, `member.mfa_locked`. Rows hold the email and, for admin actions, the reason; never a secret, a code, or a recovery code.
11. **Password reset and activation leave MFA alone.** Resetting a password never removes the second factor. Activation sets the password only; a member invited into a requiring organization enrolls at their first sign-in after it.
12. **Errors.** Sign-in family keeps its codes: `MFAREQUIRED`, `MFAINVALID`, `RATELIMITED`, `INVALIDINPUT`; new ones are `MFA_ENROLLMENT_REQUIRED` (401), `MFA_ALREADY_ENROLLED`, `MFA_NOT_ENROLLED`, `MFA_NOT_REQUIRED`, `MFA_REQUIRED_BY_ORGANIZATION` (409). Tenant Admin routes use `VALIDATION_FAILED`, `FORBIDDEN`, `NOT_FOUND`, `CANNOT_CHANGE_SELF`, `MFA_NOT_ENROLLED`, and `SESSION_REVOCATION_UNAVAILABLE` (503, the change is rolled back).
13. **Reads for the UI.** `GET organization` includes `requireMfa`; the members list includes `mfaEnabled`; `GET mfa` returns the member's own state (`enabled`, `enabledAtUtc`, `recoveryCodesRemaining`, `requiredByOrganization`, `canDisable`).

## Phase A — API

- [x] A1. `Tenant.RequireMfa` and migration `AddTenantRequireMfa`.
- [x] A2. Sign-in: `totpCode` and `recoveryCode` on `auth/login` and `auth/sign-in`; `MFAREQUIRED`, `MFAINVALID`, `MFA_ENROLLMENT_REQUIRED`; recovery codes in the enrolling response; refresh guard; shared verification, replay guard, and lockout with the Platform Admin flow.
- [x] A3. `POST auth/mfa/enrollment` (password-checked secret for a required enrollment).
- [x] A4. Signed-in `GET/POST mfa/...` (status, opt-in, confirm, regenerate, disable) in `MemberMfaController`.
- [x] A5. `PUT organization/mfa` and `POST members/{accountId}/mfa-reset`; `requireMfa` on the organization, `mfaEnabled` on the members list.
- [x] A6. OpenAPI response types on every route; audit entries; no secret in logs or audit.
- [x] A7. Tests (`MemberMfaTests` in Phase 2, `MemberMfaHttpTests` and `TenantMembersHttpTests` in Phase 1): requirement and enrolled-admin rule, forced enrollment, replay, lockout, recovery single use, regeneration, opt-in and opt-out, admin reset permission, isolation between organizations, multi-organization account, refresh guard, password reset keeps MFA. The Phase 2 smoke runs the whole flow through Gateway.

## Phase B — UI

- [ ] B1. Shell sign-in: handle `MFAREQUIRED` (code or recovery code field), `MFA_ENROLLMENT_REQUIRED` (secret, QR, first code, then the one-time recovery codes), `MFAINVALID`, and the lockout message.
- [ ] B2. Profile: MFA card with status, opt-in, regenerate recovery codes, and opt-out (hidden when `canDisable` is false).
- [ ] B3. Administration: an "Require MFA for all members" switch with a plain warning that members without an authenticator are signed out; an MFA column and **Reset MFA** (with reason) in the members list.
- [ ] B4. Playwright for the flows above; docs: [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Known Limits

- The authenticator is per account, so an admin's reset in one organization clears the factor everywhere the person is a member. An organization that requires MFA makes them enroll again; a member who had enrolled by choice and belongs to another organization that does not require it loses that protection there until they enroll again.
- An organization whose only Tenant Admin loses the authenticator and the recovery codes has no self-service path (admins cannot reset themselves, Platform Admins do not manage tenant data); it takes a database change by the operator, or a second admin.
- Platform Admins and members share `Mfa:EncryptionKey`; changing it makes stored secrets unreadable (`503 UNAVAILABLE`, not counted as a wrong code) until members are reset.
- No QR image is generated server-side; the UI draws it from the `otpauth://` URI.
- Authenticator codes are SHA-1 TOTP only; no WebAuthn or SMS.

## Out Of Scope

- A grace period or a per-group requirement (see decision 2).
- Per-member exemptions from the requirement.
- Trusted devices or "remember this browser for 30 days".
- Showing these audit entries to Tenant Admins (backlog item 8).
- Session listing and revocation (backlog item 9).

## Related

- [[Product Backlog]]
- [[Security And Tenant Model]]
- [[Tenant Member Management Plan]]
- [[Platform Admin Console Plan]]
- [[Project Status]]
