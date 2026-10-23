# Tenant Audit Log Plan

> Drafted 23 October 2026 for backlog item 8. Lets an organization's Tenant Admin read the member-management history that [[Tenant Member Management Plan]] already writes to `platform_organization_audit`. The Platform Admin view is in [[Platform Admin Console Plan]].

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| Audit rows | `platform_organization_audit` (Identity). `AddAudit` writes `member.*` actions for Tenant Admin changes and `organization.*` / `administrator.*` / `platform_admin.*` actions for Platform Admins | None for tenants |
| Platform Admin audit list | `GET /api/v1/platform/audit` (all tenants, filters, paging, newest first) | Sys-admin audit page |
| Tenant isolation | The table has a tenant query filter; `TenantMemberService.EnsureAdminAsync` checks an active admin of the token's tenant and sets the scope | n/a |

## Decisions

1. No migration: the table already has the `(TenantId, CreatedAtUtc)` index and the needed columns.
2. The route is `GET /api/v1/members/audit` in Identity (Gateway: `/api/v1/identity/members/audit`), next to the other Tenant Admin member routes and behind the same guard: a `TenantAdmin` token (403 otherwise, 401 for a missing, stale, or wrong-tenant admin).
3. Only `member.*` actions are returned. Platform Admin actions on the organization carry internal reasons and Platform Admin emails, so they stay on the platform side.
4. Filters are an exact `action` code (must start with `member.`, else 400), `fromUtc`, and `toUtc` (`fromUtc` after `toUtc` is 400). Paging is `page` (clamped into range) and `pageSize` (1-100, default 25), newest first.
5. Each entry carries the actor's email (the admin who acted), the action, and the before/after JSON, which never hold credentials, codes, or keys.

## Phase A: API

- [x] A1. `TenantMemberService.ListAuditAsync` and `GET api/v1/members/audit` with OpenAPI attributes for 200, 400, 401, 403.
- [x] A2. Contracts `TenantAuditEntry` and `TenantAuditListResponse`.
- [x] A3. Tests: only the organization's `member.*` entries, newest first, paging, filters, refusals, a second tenant seeing nothing.
- [x] A4. Smoke steps in `tests/phase2/Smoke.ps1` through Gateway.

## Phase B: UI

- [ ] B1. An Audit log panel in the Administration remote (table with action label, actor, time in the organization's date format, filter by action and dates, paging).
- [ ] B2. Human labels for the `member.*` actions, shared with the sys-admin audit labels where possible.

## Out Of Scope

- Auditing non-member tenant changes (projects, leave types, approvals).
- Exporting the log, and searching by actor.

## Related

- [[Tenant Member Management Plan]]
- [[Platform Admin Console Plan]]
- [[Product Backlog]]
