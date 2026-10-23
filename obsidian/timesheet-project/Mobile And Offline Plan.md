# Mobile And Offline Plan

> Drafted 23 October 2026 from item 14 of [[Product Backlog]] and an audit of `develop` at `62b62eb`. An installable app (PWA) in which a member records time without a connection and the changes sync when the connection returns. Phases A (API), B (installable shell), and C (offline Timesheet) are on `feature/pwa-offline`.

## Audit: What Exists On `develop`

| Area | What it does today | Consequence for offline |
|---|---|---|
| Shell (Nuxt 4, server-rendered, `src/Frontend/app`) | Signs the member in; the session (access token, refresh token, tenant, role) lives in `sessionStorage`; pages are rendered by Nitro but hold no member data in the HTML | The HTML can be cached safely; the session cannot outlive the tab or window |
| Remotes (`src/Frontend/remotes/*`) | Each is its own Nuxt app on its own origin, embedded by the Shell in an iframe; the Shell posts `aiyara.session` (token, locale, theme, formats, `tenantId`) and answers `aiyara.refresh-request` | A service worker and IndexedDB belong to an origin, so the Timesheet remote needs its own; the Shell's cannot serve the iframe |
| Timesheet remote | One page (`app.vue`) loads entries, tasks, leave, catalog, context, month state, balances, templates with `$fetch` and writes directly; the shared `TimerPanel` talks to `/timer` through the page's `api` function | Reads and the entry/timer writes can be redirected through a local store; everything else stays direct |
| Entry rules | `SaveEntry`: `CanMutate` (own current month, or a returned month until submitted, approved, locked; else `409 MONTH_CLOSED`), `CheckEntryAsync` (task name, lengths, references, `INVALID_TIME_RANGE`, `ENTRY_IN_BREAK`); the server computes the duration with the organization's break | The server stays the only judge; a queued change can be refused when it syncs |
| Entry create | `POST entries` gets its id from the server; replaying a request that timed out would create a second entry | Needs a client-supplied id |
| Timer | One server-kept timer per member (`timer/start`, `stop`, `DELETE`); `stop` accepts `date`, `startTime`, `endTime` | A timer cannot be started on the server without a connection |
| Gateway auth | Opaque access tokens (about 15 minutes) and refresh tokens; the remote asks the Shell to refresh on `401` (`withRemoteRefresh`) | Tokens expire while changes wait; the sync must refresh first. Tokens are not JWTs, so the client cannot read the account from them |
| Hosting | Each Nuxt app runs as a Nitro server in its own container (`bun .output/server/index.mjs`); no reverse proxy | `sw.js` and the manifest need correct headers from Nitro route rules |

## Decisions

### What works offline

1. **App shell.** After one online visit, the installed Shell and the Timesheet remote open without a network. The Shell precaches its two member pages (`/`, `/remote/timesheet`) and its hashed assets; the remote precaches its single page and assets. Nothing else is cached by the service workers (decision 9).
2. **Viewing.** The Timesheet remote keeps the last successful answer of each read (entries per month, tasks, leave, balances, catalog, context, month state, templates, running timer) in IndexedDB, and shows it, labelled as saved on this device, when a read cannot reach the server.
3. **Writing, queued:** create, edit, and delete a time entry (single entry form, edit from the day panel or the month list), and start, stop, and discard the running timer. Each becomes an item in an **outbox** in IndexedDB and appears at once in the lists marked **Not synced**, with an estimated duration computed by the page's copy of `TimesheetRules` (the server's figure replaces it on sync).
4. **Online only, with a clear message** ("This needs a connection."): reports, approval (submit, approve, return), Administration, leave, tasks and the board, templates, **Several days**, **Copy day or week**, profile, MFA and sessions, and the timer on Today in the Shell. The controls stay visible; the failed action says why.
5. **A cold start offline is not supported.** The session lives in the tab's `sessionStorage`, so closing and reopening the installed app needs a sign-in, which needs a network. Persisting the refresh token on disk was rejected: it would store a credential. The outbox survives the restart in IndexedDB and syncs after the same account signs in. What works is the case that matters on a phone: the app is open, the connection is lost (a tunnel, a lift, a site with no signal), and the member keeps recording, reloads, or switches apps.

### Direct when online, queued when not

6. With a connection, writes go straight to the Gateway as today, with the same error messages, so nothing changes for a member who is online. A write is queued when the device is offline, when the request fails without any response (the network, not the server), or when the entry already has a queued item (so order is kept). Creates always carry a client-generated id, so a request whose answer was lost and is then replayed from the outbox cannot create twice.
7. **Coalescing keeps the queue small.** Editing a not-yet-synced entry rewrites its queued create. Deleting a not-yet-synced entry removes its queued create (the server never hears of it). Editing an entry twice keeps one update. Deleting an entry with a queued update replaces it with the delete. An item that is being sent is never rewritten; a later change is queued after it.

### Idempotency and conflicts

8. **Idempotent create (API).** `POST entries` accepts an optional `id` (a GUID). If an entry with that id already belongs to this member, the call answers `200` with that entry and changes nothing, even if the month has since closed or the entry was deleted, so a replay is a success. An id that belongs to anyone else (another member or tenant) is `409 ID_IN_USE`, never revealing whose; two requests at once are settled by the primary key. No migration: `Id` is already the key. `GET context` also returns `userId`, which the page needs to key its store. A queued update replays harmlessly (`PUT` sets the same values), and a delete that finds the entry gone (`404`) counts as done.
9. **Conflict rules at sync.** The server decides, as for a live request, and a refusal is shown, never retried silently.
   - A `4xx` the server gives for the item's content (`MONTH_CLOSED` for a locked, submitted, or approved month, `INVALID_TIME_RANGE`, `ENTRY_IN_BREAK`, `VALIDATION_FAILED`, `404` for an entry deleted elsewhere, `ID_IN_USE`) marks that item **Failed**, keeps it, and carries on with the others.
   - A failed item shows its reason in words and offers **Fix** (opens the entry form filled with the item; saving replaces the item and queues it again) and **Discard**. A failed update whose entry no longer exists is fixed as a new entry.
   - A network failure, `5xx`, or `429` leaves the item **Pending** and stops the run; it is tried again on reconnect, on return to the tab, every minute while items wait, and by **Sync now**.
   - Edits to the same entry from two devices are last write wins, as online; the entry has no version.
10. **Timer.** Starting with no connection starts a **local timer** (its start time, task name kept in IndexedDB and shown by the same `TimerPanel`). A local timer never goes to the server: stopping it queues a create with a client id, from its start time to now in the organization's time zone (taken from the cached context, not the device's). Stopping a server-kept timer offline queues the same create plus a discard of the server timer, sent after the create is accepted (`404` counts as done). Discarding a local timer just removes it. A second device does not see a local timer.

### Auth, privacy, tenant isolation

11. **Sync never uses stored credentials.** It runs only in a live session of the Timesheet remote and sends requests through the page's existing `api`, which asks the Shell to refresh an expired token (`withRemoteRefresh`). If the refresh fails, the Shell takes the member to sign-in and the queue stays. Nothing is added to `localStorage`; no password or token is written to IndexedDB.
12. **Identity of the data.** Everything in IndexedDB is keyed by `tenantId:userId`, from the session message and `GET context`. The page uses the cache and queue of the identity it has confirmed, and with no confirmation (offline on a fresh tab, no remembered identity in that tab's `sessionStorage`) it shows no saved data and syncs nothing.
13. **Cleared on sign-out and on account switch.** Sign-out and **sign out everywhere** delete the whole offline database through a hidden iframe to the remote (`/?aiyara-purge=1`) before the session is cleared, whichever page the member is on, and the Shell asks first when changes are waiting ("N changes have not synced and will be lost"). When the page confirms an identity different from the one stored, it deletes the other identity's cache and queue before reading anything. An expired session does not clear anything.
14. **How the cache is protected.** IndexedDB is private to the remote's origin, holds only the signed-in member's own entries for the months they opened, plus the organization's catalog, and drops cached reads older than 30 days when it opens. It is not encrypted: a key kept by the same origin would not protect it from that origin, and the member's own device lock is the real protection. The plan says so plainly rather than claiming encryption.
15. **Service workers cache no member data.** Workbox precaches only the build's own files (scripts, styles, fonts, the page HTML, which carries no data). There is no runtime caching route, so no Gateway response is ever stored by a service worker and a request to the Gateway is never answered by one.

### Installable app

16. **`@vite-pwa/nuxt` 1.1.1 (pinned) for the service workers** in the Shell and the Timesheet remote. It generates the Workbox precache with content hashes and keeps it consistent with each build, which hand-written service workers get wrong (stale files, unbounded caches). It adds build-time dependencies only; `registerType: 'prompt'`, so a new version waits and never reloads a page in the middle of an entry. The Shell shows **Update available / Reload**; a remote updates when its iframe is next opened. Its manifest generation and icon tooling are not used: the manifest is a static file and the icons are committed PNGs, so installability does not depend on a plugin.
17. **Manifest and meta.** `manifest.webmanifest` (standalone, start URL `/`, `theme_color` `#155eef` from `--color-brand` and `background_color` `#eef2f7` from `--color-canvas` in `shared/tokens.css`), icons 192, 512 and a maskable 512, `apple-touch-icon`, `theme-color` for both schemes, `apple-mobile-web-app-capable`, status bar style, and `viewport-fit=cover`. `sw.js` and the manifest are served `no-cache` by Nitro route rules so a deploy is noticed at once; hashed `_nuxt` files keep their long cache.
18. **Mobile.** The member screens (Today, Timesheet with the day panel, entry form, timer, sync bar, failed items) are checked at 375 and 320 px with no horizontal scroll; the sync bar wraps.

## Phase A: API

- [x] A1. `EntryRequest.Id`; `SaveEntry` replays by id (`200`), refuses a foreign id (`409 ID_IN_USE`), settles a race on the key; the OpenAPI response lists `200`.
- [x] A2. `GET context` returns `userId`.
- [x] A3. Tests (`OfflineSyncTests`): replay returns the same entry and writes once (one entry, one audit, one outbox event), replay after the month closed, replay after delete, foreign member and tenant ids refused, two creates at once, `context.userId`.
- [x] A4. Smoke phase 3 step on a test stack: create with a client id twice.

## Phase B: Installable Shell

- [x] B1. Manifest, icons, meta tags, Nitro headers, service worker (precache `/` and `/remote/timesheet`).
- [x] B2. Shell: offline banner, update banner, online-only message for Reporting, Administration, and Profile when offline.
- [x] B3. Sign-out purge handshake and the pending-changes confirmation.

## Phase C: Offline Timesheet

- [x] C1. Timesheet remote built as a single page with its service worker.
- [x] C2. IndexedDB store (cache, outbox, local timer, purge) and the outbox rules (coalescing, replay, failure classes).
- [x] C3. Page: offline-aware reads, queued entry writes and timer, sync on reconnect, on return to the tab, every minute, and **Sync now**; **Not synced** marks; status bar with **N changes waiting to sync**; failed items with **Fix** and **Discard**; the offline message for online-only actions.
- [x] C4. Playwright: manifest served and linked, service workers register and the remotes open offline from a production build, entry created offline then synced once with its id, failed item surfaced and fixed or discarded, timer offline, sign-out clears the store, account switch clears it; 375 and 320 px.
- [x] C5. Docs: [[Project Status]], [[Product Backlog]], [[Timesheet Project Index]].

## Out Of Scope

- Opening the app offline after the tab or window was closed (needs a stored credential).
- Offline leave, tasks, templates, **Several days**, **Copy**, approval, reports, Administration, and the Home timer.
- Background Sync and push notifications (not available on iOS Safari; the sync runs while the app is open).
- Encrypting the local store, or a remote wipe of a lost device.
- Merging two devices' edits of the same entry (last write wins) and entry versions.
- Offline support in the Reporting, Administration, and identity-profile remotes.
- Native apps and an App Store or Play listing.

## Related

- [[Product Backlog]]
- [[Timesheet Page Plan]]
- [[Running Timer Plan]]
- [[Copy And Bulk Entry Plan]]
- [[Timesheet Approval Plan]]
- [[Security And Tenant Model]]
- [[Project Status]]
