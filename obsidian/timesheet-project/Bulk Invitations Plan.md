# Bulk Invitations Plan

> Drafted 23 October 2026 from item 10 of [[Product Backlog]] and a code audit of `develop` at `79a627c`. Phase A (API) implemented the same day on `feature/bulk-invitations`; Phase B (UI) implemented 23 October 2026 on `feature/bulk-invitations-ui`. Lets a Tenant Admin invite several members at once, or import them from a CSV, which [[Tenant Member Management Plan]] listed as out of scope.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| One invitation | `POST /api/v1/auth/tenants/{tenantId}/invitations` (`OnboardingService.IssueInvitationAsync`): normalizes the email, role Employee or TenantAdmin, optional `groupId`, refuses an existing member (409) and a Platform Admin account, replaces an older pending invitation, audits `member.invited`, then Identity emails the code through Notification | Administration invite form |
| Pending invitations | `GET members/invitations`, resend (new code), revoke | Administration invitation list |
| Groups | `GET members/groups` (id, name, member count); names unique ignoring case | Group picker |
| Limits | No member count limit exists; the `auth` rate limit (60 per minute per IP) guards invitation issuing | n/a |
| Gateway | `/api/v1/identity/{**catch-all}` forwards to Identity | n/a |

## Decisions

1. No migration. Bulk invitation writes the same `invitations` and `platform_organization_audit` rows as the single invitation.
2. Route: `POST /api/v1/members/invitations/bulk` in Identity (Gateway: `/api/v1/identity/members/invitations/bulk`), next to the other Tenant Admin member routes with the same guard (`TenantAdmin` token, 403 otherwise, 401 for a missing, stale, or wrong-tenant admin) and the `auth` rate limit.
3. The API takes JSON rows; the UI parses the CSV. A row is `email`, `role` (`Employee` or `TenantAdmin`, any case), and optionally `groupId` or `groupName` (matched ignoring case, since a CSV has names; `groupId` wins when both are sent).
4. Size: 1 to 100 rows. An empty list, a missing list, or more than 100 rows is `400 VALIDATION_FAILED` and nothing is created.
5. Partial success, not all or nothing: each row is judged on its own and the response is `200` with one result per row in request order. Rows that pass are saved in one transaction (so a database failure creates none of them), each with its own `member.invited` audit row.
6. Skip reasons, checked in this order: `InvalidEmail` (also blank or over 320 characters), `DuplicateInBatch` (the first occurrence of a normalized email wins), `InvalidRole`, `UnknownGroup`, `NotInvitable` (a Platform Admin account, never named as such), `AlreadyMember` (a member of this organization in any status), `AlreadyInvited` (a pending, unexpired invitation exists).
7. `AlreadyInvited` is a skip here, unlike the single invitation which replaces the older code: a bulk re-import must not silently invalidate codes the invitees already hold. The admin resends from the invitation list when a new code is wanted.
8. No email is sent for a skipped row, so an invalid address never reaches Notification. Created rows are emailed after the save; a delivery that is unavailable or rate limited does not undo the invitation and is reported as `emailSent: false`.
9. Invitation codes are never in the response; they go only to the invitee's email.
10. An address that is a member of another organization can be invited, as with the single invitation, because accounts are global and memberships are per organization.

## Phase A: API

- [x] A1. `TenantMemberService.InviteBulkAsync` and `POST api/v1/members/invitations/bulk` with OpenAPI attributes for 200, 400, 401, 403, 429.
- [x] A2. Contracts `BulkInvitationRequest`, `BulkInvitationRow`, `BulkInvitationResponse`, `BulkInvitationRowResult`, `BulkInvitationLimits.MaxRows`.
- [x] A3. Tests: every reason code, normalization, group by id and by name, the batch audited and emailed once per created row, no code in the response, refusals (empty, missing, over 100, employee, anonymous, wrong tenant), delivery failure, a second tenant seeing none of the invitations.
- [x] A4. Smoke steps in `tests/phase2/Smoke.ps1` through Gateway, including the emails in MailDev.

## Phase B: UI

- [x] B1. An **Invite several** panel (`BulkInvite.vue`, helpers in `utils/bulkInvite.ts`) in the Administration remote: paste rows, up to 100.
- [x] B2. CSV import in the browser (BOM and delimiters handled, header optional, role defaults to Employee, group by name), previewed with per-row checks before sending; more than 100 rows is blocked.
- [x] B3. A per-row result list after sending (created, or the skip reason in plain words), with the skipped rows retried in the panel or downloaded as CSV (formula-neutralised), and `emailSent: false` rows pointing to the invitation list.
- [x] B4. Playwright and docs: [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Status

Implemented 23 October 2026 on `feature/bulk-invitations` (backend `fde8441`) and `feature/bulk-invitations-ui` (UI `3b511c1`). No migration. An existing pending invitation is skipped as `AlreadyInvited`, whereas the single invite replaces it. Emails are sent after the save, only for created rows; `emailSent` is false when Notification is unavailable. Validation: .NET build 0 warnings, tests Phase1 84 / Phase2 48 / Phase3 68 / Phase4 68, Smoke phase 2 on a test stack, Playwright 109/109, `nuxt build` of the Shell and administration remotes. Not verified: the 429 path was not tested at the backend, tests ran on SQLite and not PostgreSQL, and the UI was not checked by hand on a live stack.

## Out Of Scope

- Server-side CSV upload or parsing.
- Bulk resend or bulk revoke.
- Creating missing groups from the import.
- Importing existing members' group assignments (use the member list).

## Related

- [[Tenant Member Management Plan]]
- [[Tenant Audit Log Plan]]
- [[Product Backlog]]
- [[Security And Tenant Model]]
