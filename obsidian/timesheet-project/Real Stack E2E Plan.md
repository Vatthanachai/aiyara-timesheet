# Real Stack E2E Plan

> Drafted and implemented 23 October 2026 on `feature/real-stack-e2e`, item 13 of [[Product Backlog]]. A second Playwright project drives the real Shell and remotes in a browser against a throwaway Compose test stack, so a few high-value flows are checked end to end through Gateway, Identity, Timesheet, Notification, and MailDev instead of mocked APIs.

## Audit: What Existed On `develop`

| Capability | State |
|---|---|
| Mocked Playwright | 109 tests in `src/Frontend/app/tests/e2e`; `playwright.config.ts` starts dev servers on 3100-3105 and stubs every API call. |
| Real stack | Only `tests/phase1-4/Smoke.ps1` reached it, by HTTP, with no browser. |
| Test stack | [[Test Stack Plan]]: `scripts/test-stack.ps1 up -Frontend` runs the built Shell and remotes on loopback ports, but nothing drove them. |
| Platform Admin | `BOOTSTRAP_PLATFORM_ADMIN_EMAIL` was always empty on the test stack, so no spec could sign a Platform Admin in. |

## Decisions

1. **Separate project.** `src/Frontend/app/playwright.real.config.ts`, specs in `tests/e2e-real/`, `bun run test:e2e:real`. It has no `webServer` and uses none of 3100-3105, so it can run beside the mocked suite. Its URLs come only from `E2E_REAL_*` environment variables; without them the config stops with a message naming the wrapper.
2. **One wrapper.** `scripts/test-e2e-real.ps1` resets and brings up the test stack with the frontend profile, sets the variables from `test-stack.ps1 env`, runs the specs (`-Repeat N` runs them N times on the same stack), and resets the stack at the end (`-KeepStack` and `-SkipReset` keep it). The browser opens the Shell as `localhost`, the origin Gateway CORS and the remotes allow. It never uses the main stack.
3. **Setup by API, the flow by browser.** `support/api.ts` creates an organization (public sign-up), an activated employee, or an authenticator through Gateway where that is not what the spec tests; each spec makes its own organization with unique names, so specs are independent and a stack can be reused.
4. **TOTP in the test.** `support/totp.ts` implements RFC 6238 (HMAC-SHA1, 30 s, six digits). The server accepts the current step or one either side, never a step it already accepted, so `Authenticator.code()` returns the current step, or the next one if the current was just used, and waits only when both are used up. A call at a step boundary is covered by the one-step tolerance.
5. **The Platform Admin is bootstrapped.** `test-stack.ps1` now writes `BOOTSTRAP_PLATFORM_ADMIN_EMAIL` from the environment (empty otherwise); the wrapper sets `platform-e2e@example.test`. The first spec run activates it from the email, enrolls an authenticator through the System administrator page, and keeps the secret and last-used step in git-ignored `tests/e2e-real/.state`. Later runs on the same stack sign in with a fresh code. A reset stack needs that file removed; the wrapper does it.
6. **Few specs.** Four, each one story, instead of many small ones (see below). Interactions in the Shell and in the iframes go through `until(...)` (Playwright `toPass`) because production Nuxt builds expose no hydration flag, and a field typed into before hydration is cleared again.

7. **Paced against the rate limit.** Gateway allows 60 requests a minute on the sign-in, activation, and invitation routes, and a spec uses a few dozen. `support/pace.ts` starts each spec at least 25 s after the previous one ended (also across runs, through a timestamp in `.state`), and the Gateway helper waits out a 429 and retries. A run takes about two minutes.

## Specs

| Spec | Flow |
|---|---|
| `platform-organization.spec.ts` | Platform Admin signs in with a TOTP code, creates an organization in `/sys-admin/organizations`, finds it pending; its administrator uses the MailDev code on `/activate`, signs in, and the list shows the administrator active. |
| `project-time-entry.spec.ts` | Tenant Admin adds a project in Administration; an employee signs in, saves a 09:00-11:00 entry against it in Timesheet and sees it in the month list and totals; the profile lists one signed-in device marked **This device**. |
| `bulk-invitations-audit.spec.ts` | Tenant Admin pastes two addresses into **Invite several**; both are pending invitations and both emails arrive; the Audit log lists a **Member invited** entry for each, by the admin. |
| `member-mfa.spec.ts` | Tenant Admin with an authenticator turns on **Require MFA for all members**; an employee signs in, is asked to set one up, confirms with a computed code, acknowledges ten recovery codes, signs out, and signs in again with a code. |

## Phase A: Tests And Wrapper

- [x] A1. `playwright.real.config.ts`, `test:e2e:real` script, ignored result and state directories.
- [x] A2. Support modules: stack settings and state, MailDev polling, Gateway setup calls, TOTP, UI helpers, Platform Admin sign-in.
- [x] A3. The four specs above.
- [x] A4. `scripts/test-e2e-real.ps1`; `test-stack.ps1` bootstrap email option.

## Phase B: Docs And Validation

- [x] B1. `docs/developer-runbook.md`, this note, Project Status, Product Backlog, and Index links.
- [x] B2. The specs passed three runs in a row on a reset stack (see [[Project Status]] for the commands and results); the mocked suite still passed.

## Found On The Way

- The built Shell ignored the `NUXT_PUBLIC_*_REMOTE_URL` variables that `docker-compose.yml` set: at run time Nitro maps `NUXT_PUBLIC_REMOTES_ADMINISTRATION` and its siblings to `runtimeConfig.public.remotes`, while the old names are read only by `nuxt dev`. A stack on other ports therefore embedded the default ports' remotes, which on this machine are the main stack's. `docker-compose.yml` now sets the four `NUXT_PUBLIC_REMOTES_*` names as well (same values, so the main stack is unchanged on default ports).

## Known Limits

- The specs run in Chromium only, in English, at one viewport; the mocked suite keeps covering Thai and narrow screens.
- The Platform Admin is shared across specs, so the project runs with one worker; a fresh organization per spec keeps the rest independent.
- Reports, leave, approval, and password reset are not covered by browser flows against the real stack.
- Not run in continuous integration (backlog item 11 is deferred).

## Out Of Scope

- Continuous integration.
- Replacing the mocked suite or the PowerShell smoke scripts.
- Visual regression or load tests.

## Related

- [[Product Backlog]]
- [[Project Status]]
- [[Test Stack Plan]]
- [[Member MFA Plan]]
- [[Bulk Invitations Plan]]
- [[Session Management Plan]]
- [[Tenant Audit Log Plan]]
