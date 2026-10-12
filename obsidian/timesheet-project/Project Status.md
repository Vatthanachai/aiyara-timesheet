# Project Status

> Updated 12 October 2026. Phases 0–5 are implemented and validated on `develop`. Organization-code login and UUIDv8 Identity ID allocation were merged in `3891440`. See [[Development Roadmap]].

## Recent Merges To `develop`

- `fix/phase5-login-span-name` (12 October 2026): resolves the `tests/phase5/Smoke.ps1` failure noted under `fix/english-email-templates` ("missing Gateway or Identity login spans"). Phase 5 last passed at `3300cb3` (7 October, 11:00); `e6d75db` (13:01 the same day) moved Identity from a Minimal API group (`MapGroup("/api/v1/auth")`) to an MVC controller (`[Route("api/v1")]`), so the Identity span name changed from `POST /api/v1/auth/login` to `POST api/v1/auth/login` (MVC attribute-route templates have no leading slash). Jaeger still received both Gateway and Identity spans; only the script's exact-name match broke. The script now matches `^POST /?api/v1/auth/login$`. No dashboard, alert, or other script depends on the route name. Verified against the live Compose stack: `tests/phase5/Smoke.ps1` passed twice in a row.
- `feature/platform-admin-login` (12 October 2026): sign-in no longer asks for an organization code. `POST /api/v1/auth/sign-in` takes email and password; one active organization issues a session, several return the organization list (only after the password is verified) and the Shell asks the user to pick one, then logs in through the unchanged `/auth/login`. System administrators use `/sys-admin/login` (`POST /api/v1/auth/platform-admin/login`), which rejects non-`PlatformAdmin` memberships before a session is created. Login attempts are now limited per email rather than per tenant-account. Validation: solution build 0 errors; `Aiyara.Phase2.Tests` 21/21; Shell `nuxt build` and Playwright 12/12; `git diff --check` clean; live Compose smoke for platform, phase1–4 passed (phase2–4 against this repo's MailDev on temporary ports 1026/1081 because the separate `aiyara-maildev` stack does not expose MailDev's `/email` API); new endpoints probed through Gateway (401 for invalid credentials). See [[Security And Tenant Model]].
- `fix/smoke-maildev-conflict-detection` (12 October 2026): a redeploy of the fix above, done to re-verify it, hit the exact conflict `fix/english-email-templates`'s commit message had only warned about — a local external tool unrelated to this repo took over host ports 8080/1025 mid-session, and `tests/phase2/3/4/Smoke.ps1` just retried until timeout with the generic "email was not delivered" instead of naming the real cause. Added `Assert-MailDevShape` to all three scripts (checked once, on the first poll, that a non-empty response actually has mail-shaped objects) so a port conflict now fails immediately with a diagnostic naming it. Unified all three scripts' default `-MailDevUrl` to `127.0.0.1:8080` (phase2's previous `1081` default, meant to dodge "the common local 8080 conflict," no longer matched reality once the actual conflicting instance turned out to be on 8080). Fixed a bug the new check surfaced in the same change: a single-step `@(Invoke-RestMethod ...)` wrap (added to phase3) returns the cmdlet's array result as one element instead of flattening it, unlike assigning it to a variable first — matched phase2's working two-step pattern. Verified by reproducing both failure modes live: pointed at the real conflicting service (fails fast, clear message) and at a correctly isolated MailDev on alternate ports (phase2/3/4 all pass). `readme.md` and `docs/developer-runbook.md` updated to match.
- `fix/english-email-templates` (12 October 2026): found during a clean Docker deploy (full `down -v` + rebuild + `up`), not from reading code alone. Commit `e95d86d` ("Add polished HTML email templates", 7 Oct) had rewritten `EmailTemplates.cs` to Thai-only HTML with no locale parameter, silently breaking `tests/phase2/Smoke.ps1`, `phase3/Smoke.ps1`, and `phase4/Smoke.ps1` — all three still searched MailDev for the pre-`e95d86d` English subjects (confirmed via `git show e95d86d` that those were the real original strings, not a guess). Since `e95d86d`, none of those three smoke scripts could have passed a real run to completion. Restored English copy (keeping the HTML polish), which also now matches the platform's default-English decision from `feature/shell-i18n-default-english` earlier the same day — these are pre-login transactional emails with no locale signal available at send time. Also found and fixed a second, independent bug the first fix unmasked: the smoke scripts' shared code-extraction regex `':\s*([^\s]+)\s*$'` only ever matched the original single-line email body (code as the very last token); the current multi-paragraph template never satisfies that in either language. Replaced with `'(?mi)code:\s*(\S+)'`. Verified against a live redeployed stack, not just `dotnet test`: rebuilt and redeployed `notifications-api` in place, then `tests/phase2/3/4/Smoke.ps1` all passed (were failing); `dotnet test` 56/56. Found but not fixed (unrelated): `tests/phase5/Smoke.ps1` fails consistently (reproduced twice) with a Jaeger trace-collection error, "missing Gateway or Identity login spans."
- `test/platform-admin-bootstrap-coverage` (12 October 2026): there is no dedicated "database seeder" in this repo; `Aiyara.Identities.Services/Authentication/PlatformAdminBootstrap.cs` (invoked from Identity's `Program.cs` when `BOOTSTRAP_PLATFORM_ADMIN_EMAIL`/`Bootstrap__PlatformAdminEmail` is set) is the closest equivalent — it idempotently provisions the platform tenant/account/membership row data for the first Platform Admin. Audited it against `IdentityDbContext`'s query filters (correct use of `IgnoreQueryFilters()` for the tenant lookup before tenant scope is established; the membership lookup is safely tenant-scoped by EF's automatic global filter once `tenantScope.TenantId` is set, even though the LINQ expression doesn't say so explicitly) and found no bug. First reported it as having zero test coverage; that was wrong, caused by a grep whose `\|` alternation silently failed in that shell invocation — `AuthenticationFlowTests.cs` already had `Platform_admin_bootstrap_requires_email_activation`, passing before this branch. Added the three cases that test didn't cover: a true no-op for a blank/unset configured email, `InvalidOperationException` for a malformed one, and the organization-code backfill branch for a pre-existing platform tenant row. `dotnet test` passed 56/56 (was 51, +5 new cases).
- `fix/directory-name-typos` (12 October 2026): `git mv`'d the three directories whose name was misspelled relative to their own already-correct project file, assembly, and namespace — `Aiyara.Timesheet.Componenet.Abstractions`→`...Component.Abstractions`, `Aiyara.Timesheet.Componenet.Data`→`...Component.Data`, `Aiyara.Identities.Servives`→`...Identities.Services` — and updated every `ProjectReference` path (9 `.csproj` files) and the `.slnx` to match. No C# source needed editing. `dotnet build` passed (24 projects, 0 errors, 31 warnings, unchanged); `dotnet test` passed 51/51.
- `refactor/split-common-extension` (12 October 2026): the ask was to split `CommonExtension.cs` (899 lines, 54 methods including overloads) into organized files. Before organizing anything, checked every method name against the whole solution — extension methods aren't referenced by class name, so plain usage grep is reliable once false positives from same-named BCL/LINQ methods (`.ToList()`, `.ToDictionary()`, `.AddRange()`, `.Order()`, `Convert.ToHexString`) are read and ruled out. Only 4 of the 39 distinct method names were actually called anywhere: `IsNullable`, `IsEmpty(string)`, `GetTryService`, and `GetOptions<TModel>` (missed on the first grep pass because its two real call sites use the generic form `.GetOptions<T>(...)`, not `.GetOptions(...)`). So the real fix was keeping those 4, split by purpose into `NullCheckExtensions.cs`, `ServiceProviderExtensions.cs`, and `ConfigurationExtensions.cs`, and deleting the other ~50 unused overloads with them — all in the same namespace, so no caller needed any change. `dotnet build` passed (24 projects, 0 errors, **31 warnings, down from 69** — the removed dead code carried most of the nullable-reference warnings); `dotnet test` passed 51/51.
- `feature/shell-i18n-default-english` (12 October 2026): extended `shared/i18n.ts` to the Shell app (`src/Frontend/app`), which previously had its own ~90 ad-hoc `locale === 'th' ? ... : ...` ternaries across `layouts/default.vue` and five pages — the follow-up flagged by the `refactor/frontend-i18n-keys` entry below. Also switched the platform's default language from Thai to English: `useLocale.ts`'s `defaultLocale` and the four remotes' pre-session fallback locale are now `'en'`. `useRemoteNavigation.ts`'s `label`/`description` are now `{ th, en }` objects indexed directly by locale instead of parallel `label`/`labelEn` fields. Translated every Thai selector/assertion in `tests/e2e/session.spec.ts` to English to match the new default, fixing two genuine ambiguous-match issues the translation surfaced (a duplicate "Time entries" heading at two levels; "New password" matching as a substring of "Confirm new password"). Verified with a production build of the Shell and all four remotes, Playwright E2E (9/9 passed), and `dotnet test` (51/51, backend untouched). Flagged as a possible `safeError()` product-copy bug at the time ("Session expired..." shown for a failed login); `fix/login-failure-mock-contract` (12 October 2026) found it was actually a test-mock-fidelity gap, not an application bug — Identity's real `AuthenticationFailure.InvalidCredentials` response carries `errorCode: "INVALIDCREDENTIALS"`, which `safeError()` already maps to "Incorrect email or password."; the Playwright mock omitted `errorCode`, so it fell through to the generic-401 branch instead. Corrected the mock to match the real `ServiceErrorResponse` shape; `bun run test:e2e` passed 9/9.
- `refactor/frontend-i18n-keys` (12 October 2026): added `src/Frontend/shared/i18n.ts` (`readSessionLocale`, `useLocaleText`) so the four remotes derive their Thai/English dictionaries one way instead of each hand-rolling its own `locale.value === 'th' ? {...} : {...}` wrapper. Also closed every ad-hoc inline ternary found outside those dictionaries: a dead `locale === 'th' ? 'WORKSPACE SETTINGS' : 'WORKSPACE SETTINGS'` ternary in the administration remote, its two password-policy unit labels, and eight scattered strings in the timesheet remote (section subtitles, a save notice, a delete-confirmation prompt, and two empty-state hints) — all now read from the dictionary like every other string. Did not touch the Shell app (`src/Frontend/app`), which has roughly 90 of the same ad-hoc ternaries across `layouts/default.vue` and four pages; that is a separate, much larger follow-up, not attempted here. Also did not extract `timesheet/app.vue` into smaller components — on inspection its 1078 lines are ~280 lines of script/template and ~800 lines of scoped CSS, which is ordinary for a single-file component rather than the monolith it looked like from a line count alone. Verified with a production build of all four remotes (`bun run build`), each passing.
- `fix/openapi-response-metadata` (12 October 2026): every business controller action across Gateway, Identity, Timesheet, Report, and Notification now declares `[ProducesResponseType]` for its real success/error envelope shapes, closing the item the [[Full Api Controllers Plan]] merge had left open. `dotnet build` passed (32 projects, 0 errors, 71 warnings — the pre-existing warning count, unchanged); `dotnet test` passed 51/51; `git diff --check` and `docker compose config --quiet` passed.
- `feature/sysadmin-organization-lifecycle` (merged as `75bbc61`): the approved Platform Admin organization lifecycle plan — editable organization name/slug, administrator invitations and activation resend, suspend/reactivate, lifecycle enforcement, and audit records. See [[Platform Organization Management Plan]]. The organization code remains immutable; suspension retains tenant records and blocks login, refresh, and Gateway access validation. Validation on this branch: full solution build succeeded; Phase 1–4 tests passed 51/51; focused SysAdmin Playwright E2E, Nuxt production build, Compose config, and EF model/migration consistency checks passed.

Note: the solution build currently reports 31 warnings (71 at various points earlier on 12 October 2026, down to 69 after removing two unreferenced email templates, now 31 after `refactor/split-common-extension` removed the dead code that carried most of them); "zero warnings" claims in earlier validation entries on this page predate a warning-count regression that was never isolated to a specific change and has not been re-chased since.

## Phase 0 Platform Baseline

- The Compose topology includes backend APIs, Report Worker, frontend Shell and
  remotes, PostgreSQL, Redis, RabbitMQ, RustFS, optional MailDev, Prometheus,
  and Grafana. Long-running containers have health checks; Phase 1's one-shot
  database provisioner is checked for successful completion.
- APIs and Report Worker share structured JSON logging, redaction support,
  OpenTelemetry instrumentation, liveness, and dependency reachability checks.
- AppHost represents Compose infrastructure as external resources and provides
  dependency endpoints to locally launched application projects. It also hosts
  the Scalar API reference.
- `tests/platform/Smoke.ps1` verifies the runnable Compose stack and API docs.
  Phase 0 TCP readiness probes establish reachability; Phase 1 adds
  authenticated database connectivity checks.
- Validation on 6 October 2026: solution restore/build and Compose configuration
  passed; all 17 core containers became healthy; backend health and OpenAPI
  paths passed the smoke test. With Redis stopped, Identity returned 200 from
  `/alive` and 503 from `/health`, then recovered after Redis restarted.
  AppHost published a manifest containing the external Compose endpoints and
  Scalar reference. The solution build has 76 existing warnings and no errors.

## Implemented Foundation

- .NET solution structure, central build configuration, and central package version management.
- Aspire AppHost that launches the backend applications and a Bun-powered Nuxt application.
- Compose PostgreSQL with persistent storage and separate service databases.
- Shared operational defaults applied to all backend applications.
- Gateway and Report API projects, each exposing the template `WeatherForecast` controller.
- Cross-cutting utility types for data access, API conventions, security/PASETO, Swagger, logging, and email.

## Phase 1 — Contracts, Tenancy, And Edge

- Versioned Gateway onboarding REST contracts, Identity validation/profile
  lookup protobuf contract, and RabbitMQ envelope/event/command types with
  message, correlation, idempotency, and tenant identifiers.
- Separate PostgreSQL service logins, EF contexts and initial migrations for
  Identity, Timesheet, Reporting, and Notification. Identity's tenant-owned
  entities fail closed under tenant query filters; direct service credentials
  cannot connect to another service database.
- Tenant creation and one-time invitation acceptance create pending memberships
  with Tenant Admin or Employee role. Identity requires a tenant administrator
  to issue an invitation. Phase 1's seven-day onboarding key is returned once
  at tenant creation, stored only as a hash, and authorizes a tenant-bound
  invitation route until Phase 2 replaces it with login-based authorization.
  It proves possession of the creation response, not ownership of the submitted
  admin email; the membership remains pending until activation.
  New tenant IDs originate in Identity;
  invitation tenant IDs come from the stored invitation hash, not request
  headers. Tenant-owned queries return nothing without an established scope.
- Gateway routes the four APIs, strips caller-supplied tenant/user headers,
  uses Identity gRPC validation for protected routes, and provides CORS, rate
  limits, one Scalar portal, and links to each service's OpenAPI document.
  Identity's Phase 1 validation service deliberately rejects tokens until the
  Phase 2 identity flow exists.
- `Aiyara.Phase1.Tests` covers tenant isolation, create/invite/accept rules,
  and gRPC/messaging contract shapes. A Compose-backed check creates two tenants,
  issues and accepts an invitation through Gateway, and rejects cross-tenant keys.
- Primary-agent validation on 6 October 2026: `dotnet build Aiyara.Timesheet.slnx`
  passed with 76 pre-existing warnings and no errors; `dotnet test
  Aiyara.Timesheet.slnx` passed 5/5; `docker compose --env-file .env.example
  -p aiyara-phase1-test config --quiet` passed; the full Compose stack passed
  `tests/platform/Smoke.ps1` (including the Redis failure/recovery check) and
  `tests/phase1/Smoke.ps1`; `git diff --check develop...HEAD` passed. The
  validation diff was reviewed for service ownership, tenant isolation,
  secrets, and build artifacts. Phase 2 owns token-backed request-scope
  resolution, activation, login, and email delivery.
- Phase 1 completion follow-up on 6 October 2026: the seed-only invitation
  fixture was replaced with the tenant-bound onboarding-key issuance API.
  `dotnet test Aiyara.Timesheet.slnx` now passes 7/7; the rebuilt Compose
  Identity/Gateway stack passes `tests/phase1/Smoke.ps1` for create → issue →
  accept, missing/cross-tenant keys, spoofed tenant headers, invitation replay,
  DB role isolation, protected routes, and OpenAPI discovery. Identity's
  onboarding-capability migration reports no pending model changes.

## Phase 2 — Identity And Notification

- Identity provides email activation, Argon2id password storage, tenant password
  policies, PASETO v4.public login, rotating refresh tokens, password reset and
  authenticated password change. The Shell's `/activate` page redeems the
  email code and sets a policy-compliant password, with localized safe errors.
  Tenant creation and invitation acceptance automatically request activation
  email delivery; a newly bootstrapped Platform Admin does the same when
  Notification is ready.
- Gateway validates protected tokens through Identity gRPC and caches positive
  results in Redis by token ID and session version. Revocation and stricter
  policy changes invalidate sessions; the gRPC policy version prevents an
  in-flight pre-policy validation from being cached after the change. Redis
  limits credential attempts per
  tenant/account alongside the edge IP limit.
- Notification sends credential templates through local MailDev SMTP and stores
  delivery outcomes without recording the secret. Phase 2 unit tests pass
  11/11; Compose smoke covers registration, invitation, activation, login,
  refresh replay, logout, reset, policy migration, and rate limiting.
- Login uses a unique seven-character organization code (four-character
  organization prefix plus three digits) with email and password. `AIYA001` is
  reserved for Platform Admin. Identity returns the internal tenant GUID after
  login for session and service calls; new Identity entity GUIDs use the shared
  UUIDv8 generator with random region/shard IDs from 1–9. Existing tenant codes
  are backfilled by migration `AddTenantOrganizationCode`.
- Primary-agent validation on 6 October 2026: solution build passed with no
  errors (74 existing utility warnings); Phase 1/2 tests passed 18/18;
  Identity migrations have no pending model changes; Compose configuration,
  Phase 1 and Phase 2 smoke tests, container health, diff whitespace checks,
  and Obsidian links passed. The feature diff was reviewed against the roadmap
  and repository standards before merge into `develop`.
- Follow-up merged to `develop` as `3891440`: organization-code login, tenant
  code migration and uniqueness constraint, UUIDv8 Identity IDs, login UI and
  session updates. Phase 1/2 tests, Identity API build, frontend production
  build, and Playwright E2E passed.

## Phase 3 — Timesheet Core (complete)

- Identity stores editable first/last name, HTTPS photo URL, and job title;
  its profile API verifies tenant membership and current session version.
- Timesheet owns tenant-filtered projects, categories, holidays, personal tasks,
  leave and time entries. Employees can edit only their own current-month data
  using the tenant timezone validated by Identity in both Gateway and Timesheet.
  Soft deletion, audit data, confirmed RabbitMQ month-change events, month
  locking, and immutable per-person month snapshots are implemented with
  migrations and Phase 3 unit tests.
- The Shell embeds Identity/Profile, Timesheet and Administration remotes.
  Thai/English controls, responsive time table, inline editing, task drag-in,
  leave management and tenant catalog/policy screens are implemented.
- Primary validation on 7 October 2026: solution tests 34/34, four changed
  frontend production builds, Compose configuration and all 17 service health
  checks, Phase 2 and Phase 3 Gateway smoke tests, direct Timesheet header
  spoof rejection, RabbitMQ delivery and outbox publish state, and
  `git diff --check` passed. Report-event consumption belongs to Phase 4.

## Phase 4 — Reporting (complete)

- Phase 4 reporting APIs, tenant-local schedule dispatch, durable RabbitMQ
  consumers, PDF/XLSX generation from immutable locked-month snapshots, RustFS
  storage, signed monthly PDF versioning, retention/audit handling, and the
  Thai/English Reports remote are implemented on `develop`; Phase 4 sign-off
  tests are on `feature/phase-four-signoff-validation`.
  Calendar, tenant-isolation, snapshot parsing, and PDF/XLSX renderer tests
  pass; Compose services and Gateway's unauthenticated reporting boundary are
  healthy. Report-ready email delivery is integrated through Notifications and
  delivery outcomes are recorded. Phase 4 HTTP integration tests cover the
  production reporting-auth middleware using a test Identity validator, report
  request creation, PDF rendering, signed upload/download, and audit recording.
  Notification retries for transient failures and stop-on-permanent-failure are
  covered. The Compose E2E also passed with an Identity-issued PASETO, locked
  Timesheet source data, worker PDF generation to RustFS, signed upload/download,
  and a report-ready email received in MailDev. `tests/phase4/Smoke.ps1` is the
  repeatable smoke test. Phase 4 is complete.

## Phase 5 — Quality, Operations, And Hardening (implemented; validation passed)

- Unit/contract tests and Compose smoke tests cover onboarding, identity,
  timesheet, reporting, signed uploads, tenant isolation, session revocation,
  retry, and database ownership. Playwright browser tests cover session restore,
  reload, logout, and failed login. Phase 4 retention tests cover expired object
  deletion, snapshot cleanup/immutability, and audit records.
- The six backend processes export HTTP, .NET runtime, readiness, report-run,
  authentication, RabbitMQ queue, Quartz execution, and RustFS operation metrics
  over OTLP to Prometheus. Grafana provisions ten panels. Jaeger receives OTLP
  traces; its UI is bound to loopback and the default all-in-one storage is
  transient memory. Phase 5 smoke verifies a real Identity trace and checks
  that a generated probe password is absent from trace data and service logs.
- Validation on 7 October 2026: Phase 1–5 Compose smoke tests passed against
  the isolated stack; Phase 4 generated a PDF, signed version, and report-ready
  email. Solution tests passed 41/41, Playwright E2E passed 2/2, the frontend
  production build and solution build passed, and Compose configuration passed.
  The platform smoke previously passed with all 17 core containers healthy.
  The default Compose project has
  an existing PostgreSQL volume whose stored password differs from current
  `.env`; it was left intact.
- Merge-gate review on 7 October 2026: branch scope, tenant isolation, service
  ownership, secrets, generated artifacts, conflict markers, and diff whitespace
  passed inspection. The refreshed Phase 4 live smoke confirmed Notification's
  persisted delivery outcome; Phase 5 smoke found the exact Gateway/Identity
  login trace and checked its password was absent from both traces and logs.
  Telemetry collectors no longer gate backend startup.
- `docs/developer-runbook.md` documents Compose startup, local email inspection,
  migrations, Scalar/OpenAPI discovery, test commands, and troubleshooting.

## Agreed Direction

The target solution is now defined as a multi-tenant timesheet platform with a Docker Compose local runtime. See [[Target Architecture]], [[Security And Tenant Model]], and [[Development Roadmap]] for the agreed design and delivery order.

## Related

- [[Timesheet Project Index]]
- [[Architecture]]
- [[Services And Projects]]
- [[Target Architecture]]
- [[Development Roadmap]]
