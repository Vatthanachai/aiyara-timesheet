# Session Management Plan

> Drafted 23 October 2026 from item 9 of [[Product Backlog]] and a code audit of `develop` at `4273789`. Phase A (API) is implemented on `feature/session-management`; Phase B (UI) is open. Covers how a signed-in member sees where their account is signed in and ends those sessions.

## Audit: What Exists On `develop`

| Capability | Backend | UI |
|---|---|---|
| Refresh sessions | `refresh_sessions` (one row per issued refresh token: account, tenant, token hash, `SessionVersion`, created, expires, `RevokedAtUtc`, `ReplacedById`); every refresh revokes the old row and creates a new one | None |
| Account-wide sign-out | `Account.SessionVersion` bump plus a revocation published to Gateway: used by logout, password change and reset, MFA reset, a stricter password policy | Logout in the Shell |
| Replay defence | A refresh with an already revoked token revokes every session of the account | — |
| Device and last-used data | None: no User-Agent, no sign-in time; the access token does not say which session it belongs to | — |
| Per-session revoke | None; `LogoutAsync` also bumps `SessionVersion`, so signing out one device signs out all | — |

## Decisions

1. **Scope is the caller's own account in the caller's own organization.** Routes read the account and organization from the verified access token; a session id of another account or of another organization is `404 NOT_FOUND` (the tenant query filter plus an account filter), never `403`. Platform Admins may manage their own sessions; this is not tenant data.
2. **An "active session" is a refresh row that is not revoked, not expired and issued under the account's current `SessionVersion`.** Rotation leaves one active row per sign-in, so the list has one entry per device login.
3. **New columns `refresh_sessions.UserAgent` (300) and `SignedInAtUtc`**, migration `AddRefreshSessionDevice` on top of `AddTenantRequireMfa`. A middleware puts the request's User-Agent in a scoped `SessionDeviceContext`; `CreateSessionAsync` stores it. A rotation copies `SignedInAtUtc` and the User-Agent forward. No IP address is stored (privacy, and Gateway sits in front).
4. **Device label** is derived server-side by `SessionDeviceName` ("Chrome on Windows", "Safari on iOS", "Unknown device"). The raw User-Agent is not returned.
5. **Last used** is the creation time of the active refresh row, which a client renews about every access-token lifetime (15 minutes). It is "last renewed", honestly named `lastUsedAtUtc`.
6. **The current session is identified by a new `sid` claim** in the access token (`PasetoTokenClaims.SessionId`, the refresh row the token was issued with). Tokens issued before this change have none: they mark nothing as current and `revoke-others` answers `409 SESSION_UNKNOWN`; signing in again fixes it.
7. **Routes** (Identity `api/v1/sessions`, Gateway `/api/v1/identity/sessions`): `GET` list; `DELETE {sessionId}` ends one other session (`204`); `POST revoke-others` ends all but the current (`204`); `POST revoke-all` signs out everywhere (`204`).
8. **Ending the current session through `DELETE` is `409 CURRENT_SESSION`**; the UI uses the existing logout for that.
9. **Revoking one session does not touch `SessionVersion`.** It revokes the refresh row only, so the member's other devices keep working. The revoked device's access token stays valid until it expires (at most 15 minutes) because access tokens are checked against the account version, not per session. `revoke-all` bumps the version and publishes it to Gateway, so every access token stops at once, in every organization.
10. **A refresh with a session revoked on purpose fails alone.** `RefreshAsync` used to treat any revoked token as a replay and sign the whole account out; that now applies only to a rotated token (`ReplacedById` set). A session revoked by the member or by a policy change simply gets `401`, so ending one device cannot sign out the others when that device retries.
11. **`revoke-others` is limited to the caller's organization** like the list; `revoke-all` is account-wide.
12. **Audit** (in the organization's log): `member.session_revoked`, `member.other_sessions_revoked`, `member.signed_out_everywhere`; the row holds the email only.
13. **Errors:** `UNAUTHORIZED` (401, no or invalid token, forced password change), `INVALIDCREDENTIALS` (401, stale session version), `NOT_FOUND` (404), `CURRENT_SESSION` and `SESSION_UNKNOWN` (409).

## Phase A — API

- [x] A1. `RefreshSession.UserAgent`, `SignedInAtUtc`; migration `AddRefreshSessionDevice`.
- [x] A2. `sid` claim in access tokens; `SessionDeviceContext` and its middleware; device and sign-in time stored and carried through rotation.
- [x] A3. `AuthenticationService` session methods and `SessionsController` (list, revoke one, revoke others, revoke all) with OpenAPI response types.
- [x] A4. Refresh: a deliberately revoked session fails without signing the account out.
- [x] A5. Tests (`SessionsHttpTests` in Phase 1): list and current flag, device labels, rotation keeps one row, own-session revoke, another account and another organization `404`, current session `409`, revoke-others, revoke-all, refresh of a revoked session leaves the others, expired sessions hidden. Phase 2 smoke covers the routes through Gateway.

## Phase B — UI

- [ ] B1. Sessions card in the Profile page (identity-profile remote): device, signed in, last used, "This device" badge, **Sign out** per other session with a confirm.
- [ ] B2. **Sign out other devices** and **Sign out everywhere** (the latter returns to the sign-in page); handle `404` (already gone) by refreshing the list and `409 SESSION_UNKNOWN` by asking to sign in again.
- [ ] B3. Playwright for list, one, others, and everywhere; docs-keeper updates [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Known Limits

- A session ended with `DELETE` or `revoke-others` keeps a valid access token for up to 15 minutes; only `revoke-all` cuts access tokens at once.
- "Last used" is the last token renewal, not the last request.
- The device label is a hint from the User-Agent, which a client can set freely.
- Sessions of the account in other organizations are not listed or ended individually; `revoke-all` ends them.

## Out Of Scope

- IP address or location of a session.
- Alerting on a new sign-in.
- Per-session access-token revocation.

## Related

- [[Product Backlog]]
- [[Security And Tenant Model]]
- [[Member MFA Plan]]
- [[Project Status]]
