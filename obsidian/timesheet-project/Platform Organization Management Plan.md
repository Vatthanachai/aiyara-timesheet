# Platform Organization Management Plan

> Approved 7 October 2026. Implementation branch `feature/sysadmin-organization-lifecycle`, merged to `develop` as `75bbc61`; implemented in full, no open gap remains.

## Implementation Progress

- Implemented tenant status, an audit table, Platform Admin guarded organization operations, and activation resend audit.
- Added the Identity migration with existing tenants defaulting to `Active`.
- Added the SysAdmin edit, admin invite/resend, suspend/reactivate, reason, and status controls.
- Validation: solution build passed; all 51 Phase 1–4 tests passed; the focused SysAdmin Playwright flow, Nuxt production build, Compose config validation, and EF pending-model check passed.

## Goal

Give Platform Admins a secure way to manage tenant organizations across the platform. Tenant Admins continue to manage only settings inside their own tenant.

## Agreed Scope

- Edit an organization's display name and slug, enforcing uniqueness.
- View and manage organization administrators, including inviting or re-inviting administrators and reviewing activation state.
- Suspend and reactivate organizations without deleting tenant data.
- Keep the seven-character organization code visible but immutable because users rely on it to select their organization at login.
- Record actor, target tenant, timestamp, reason where supplied, and before/after values for administrative changes.
- Keep Platform Admin authorization and tenant targeting on the server. A client-provided tenant ID is only a target identifier and never establishes tenant scope.

## Gap Closed (12 October 2026)

This section originally listed the pre-implementation gap; every item is now closed on `develop`:

- `Tenant` now has `TenantStatus` (`Active`/`Suspended`) with a safe `Active` default for pre-existing tenants (`Aiyara.Identities.Models/Tenancy/Tenant.cs`).
- The former `PlatformOrganizationEndpoints` listing-only surface is now `PlatformOrganizationsController`: list, update name/slug, suspend/reactivate (`PUT .../status`), invite administrator, and resend administrator activation.
- The SysAdmin page (`src/Frontend/app/app/pages/sys-admin/organizations.vue`) has edit, administrator-management, and suspend/reactivate controls, covered by a dedicated Playwright test (`session.spec.ts`: "platform admin can manage organization details, administrators, and lifecycle").
- `AuthenticationService` checks `tenant.Status != TenantStatus.Active` before activation, login, refresh, password reset, and policy changes, so a suspended tenant's login/refresh/activation calls fail immediately rather than only on next token validation.

## Delivery Plan

### 1. Domain And Persistence

- Add an explicit tenant status (`Active` / `Suspended`) with a migration and a safe default of `Active` for existing tenants.
- Keep the reserved platform tenant protected from ordinary tenant lifecycle changes.
- Add an audit record for organization metadata, administrator, and lifecycle changes. Do not store credentials or activation tokens in audit payloads.

### 2. Identity Application And API

- Extend the platform organization use case to retrieve details, update name/slug, manage administrator invitations, and suspend/reactivate tenants.
- Add validated request/response contracts and stable error codes. Preserve the immutable organization code and tenant ID.
- Put business routes behind Platform Admin authorization and revalidate the account, active platform membership, and session version in Identity.
- Ensure cross-tenant reads and writes occur only through the Platform Admin use case; tenant-scoped services remain tenant-bound.
- Make suspension block login and refresh and invalidate/reject active sessions through the existing Identity/Gateway revocation path.
- Keep existing tenant data and memberships on suspension; reactivation restores access without data recreation.

### 3. SysAdmin Experience

- Add an organization detail/edit panel for name and slug.
- Add administrator actions for inviting/re-inviting an admin and reviewing activation state.
- Add suspend/reactivate controls with a clear confirmation and resulting state shown in the list.
- Keep organization code read-only and show field-level validation and safe localized API errors.

### 4. Validation

- Add unit/integration coverage for role enforcement, tenant isolation, uniqueness conflicts, lifecycle transitions, login/refresh behavior, and audit data.
- Add frontend coverage for viewing, editing, inviting, resending activation, and suspend/reactivate flows.
- Build the .NET solution and affected Nuxt apps; run affected tests, `git diff --check`, conflict-marker and generated-artifact checks.
- Validate Compose configuration and exercise affected API/health paths in the deployed stack where feasible.

## Acceptance Criteria

- Only an active Platform Admin can manage organizations globally.
- Tenant Admins and Employees cannot use platform organization operations.
- Editing one organization cannot read or mutate another organization accidentally; every operation targets the requested tenant explicitly.
- Organization name and slug updates persist and reject duplicates. Organization code and tenant ID remain unchanged.
- A suspended organization cannot start or renew sessions, and already-issued access is rejected promptly; its records remain intact.
- Reactivation restores login while preserving organization data and audit history.
- Admin actions are visible in the audit record without exposing secrets.
- The SysAdmin UI supports the approved operations with clear success, validation, and failure feedback.

## Related

- [[Timesheet Project Index]]
- [[Security And Tenant Model]]
- [[Organization Codes And Identity IDs]]
- [[Development Roadmap]]
- [[Project Status]]
