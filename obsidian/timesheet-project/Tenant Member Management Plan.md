# Tenant Member Management Plan

> Drafted 20 October 2026 from a code audit of `develop` at `ee61a7b`, approved, and fully implemented the same day (phases A–C). Covers how an organization's Tenant Admin invites and manages its members. The Platform Admin side is in [[Platform Admin Console Plan]].

## Implementation Progress

- **Phase A** (invitation UI on existing APIs): implemented on `feature/member-invitations-ui`. The Shell has `/accept-invitation` (linked from sign-in and from the invitation email), and the Administration remote has a Members panel with the directory and an invite form. The panel marks your own row by comparing `profile/me` with the directory, until Phase B returns that flag. The Close month form now wraps at 320 px, which the new narrow-width check found.
- **Phase B** (member management APIs): implemented on `feature/member-management-api`. `TenantMemberService` and `TenantMembersController` (`api/v1/members`, which replaces `ProfileController.Members` and adds `isCurrentUser`); the Tenant Admin check moved into `TenantAdministrators.IsActiveAdminAsync`, shared with the existing invitation route, and now also requires an active tenant. A wrong role answers 403 as before; an unknown or stale admin answers 401. The `.http` files stay anonymous-only, because `tests/local/HttpFiles.ps1` expects every request in them to answer 200.
- **Phase C** (member management UI): implemented on `feature/member-management-ui`. The Members panel shows actions on every row but your own (Make admin / Make employee, Resend activation for pending members, Deactivate with a confirming second click, Reactivate) and a Pending invitations list (expiry, inviter, Resend, Revoke). After every action the lists reload, so a refused or partly applied action still shows the real state. The panel now uses the directory's `isCurrentUser` instead of comparing `profile/me`.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| List members | `GET /api/v1/identity/members` (`ProfileController.Members`, `TenantAdmin` only, scoped to the token's tenant) | Only the Reporting remote's member picker |
| Invite a member | `POST /api/v1/auth/tenants/{tenantId}/invitations` (`IssueAuthorizedInvitation`): active `TenantAdmin`, current session version, password not due for change; role `Employee` or `TenantAdmin`; 7-day code emailed through the `invitation` template | None |
| Accept an invitation | `POST /api/v1/invitations/accept` (anonymous): creates a `PendingActivation` membership and sends the activation email | None. The invitation email has no page link; its template comment says invitations are accepted during signup, but `signup.vue` only creates organizations |
| Change role, deactivate, reactivate | Platform Admin only, and only for administrators (`administrator.demoted` / `administrator.removed`) | Sys-admin organization detail page |
| Pending invitations (list, resend, revoke) | None | None |

Observations:

- The "already a member" check in invitation and acceptance runs under the invited tenant's query filter, so an account that belongs to another organization **can** be invited. Accepting then makes a `PendingActivation` membership, and activation sets a new account-wide password checked only against the inviting organization's policy, which can weaken the password the account uses elsewhere.
- A `Deactivated` member cannot be invited back: the invitation is refused as a conflict, and nothing reactivates a membership (the same known limit as in [[Platform Admin Console Plan]] Phase C).
- Inviting the same email twice leaves both codes valid; the Platform Admin path expires older pending invitations, the tenant path does not.
- Tenant invitations are not audited; Platform Admin invitations are (`administrator.invited`).

## Decisions

1. Members are deactivated, never deleted. Their timesheets, reports, and audit history stay.
2. A Tenant Admin cannot change their own role or status (`CANNOT_CHANGE_SELF`), and no change may leave the organization without an active `TenantAdmin` (`LAST_ADMINISTRATOR`); pending administrators do not count.
3. A role or status change ends the member's sessions everywhere (`EndSessionsAsync`), because the session version is per account.
4. Tenant Admin member actions are written to the existing `platform_organization_audit` table with `member.*` actions, so Platform Admins see them in the audit log. A tenant-facing audit view is out of scope.
5. Acceptance stays a separate step from activation: the invitee enters the code on `/accept-invitation`, then activates from the emailed code. An account that already has a password (it belongs to another organization) skips activation: its new membership becomes `Active` at once and it signs in with its existing password.

## Phase A — Invitation UI On Existing APIs

Branch `feature/member-invitations-ui`. No API change.

- [x] A1. Shell page `/accept-invitation`: enter the invitation code, call `POST /api/v1/invitations/accept`, then explain that an activation email is on its way with a link to `/activate`. Invalid, used, or expired codes and conflicts show safe messages. The sign-in page links to it.
- [x] A2. The invitation email links to `/accept-invitation` (`Open the invitation page`), like activation and reset.
- [x] A3. Administration remote **Members** panel: the member directory (name or email, role, status, a "You" marker) and an invite form (email and role) that calls the existing invitation route and confirms who was invited.
- [x] A4. Tests: Playwright for the accept page (success, invalid code) and the Members panel (list, invite, rejected invite); the phase 2 smoke test checks the invitation email's page link (no Notifications unit-test project exists; the templates were also rendered once in a scratch console app). Docs: [[Project Status]].

## Phase B — Member Management APIs

Branch `feature/member-management-api`. Identity gets a `TenantMembersController` under `api/v1/members` (Gateway `/api/v1/identity/members/...`, policy `TenantMember`), guarded like `IssueAuthorizedInvitation`: active `TenantAdmin`, matching session version, no forced or overdue password change.

- [x] B1. Move `GET /members` into the new controller; add `GET /members/invitations` (pending, unexpired: email, role, invited by, expiry).
- [x] B2. `POST /members/invitations/{id}/resend` (expire the old code, issue and email a new one) and `DELETE /members/invitations/{id}` (revoke). A new invitation for an email expires that email's older pending invitations.
- [x] B3. `PUT /members/{accountId}/role` (`Employee` or `TenantAdmin`).
- [x] B4. `PUT /members/{accountId}/status` (`Active` or `Deactivated`). Reactivating an account with a password restores `Active`; without one it returns to `PendingActivation` and the activation email is sent again.
- [x] B5. `POST /members/{accountId}/activation` re-sends activation to a pending member.
- [x] B6. Accepting an invitation with an account that already has a password activates the membership at once (decision 5) and sends no activation email.
- [x] B7. Audit actions `member.invited`, `member.invitation_resent`, `member.invitation_revoked`, `member.role_changed`, `member.deactivated`, `member.reactivated`, `member.activation_resent`, with labels in the sys-admin audit list.
- [x] B8. Tests: service tests for every rule in decisions 2–3 and tenant isolation (another tenant's member or invitation is `NOT_FOUND`); HTTP guard tests; OpenAPI from the controller attributes; phase 2 smoke covers invite, list, role change, deactivate, and reactivate through Gateway.

## Phase C — Member Management UI

Branch `feature/member-management-ui`.

- [x] C1. Members panel actions: change role, deactivate and reactivate (with confirmation), re-send activation to pending members. Your own row has no actions.
- [x] C2. Pending invitations list with resend and revoke.
- [x] C3. Error messages for `LAST_ADMINISTRATOR`, `CANNOT_CHANGE_SELF`, and conflicts; Playwright coverage including narrow widths. Docs: [[Security And Tenant Model]], [[Project Status]].

## Out Of Scope

- A tenant-facing audit log.
- Bulk invitations and CSV import.
- Transferring a member's data between organizations.

## Related

- [[Security And Tenant Model]]
- [[Platform Admin Console Plan]]
- [[Development Roadmap]]
- [[Project Status]]
