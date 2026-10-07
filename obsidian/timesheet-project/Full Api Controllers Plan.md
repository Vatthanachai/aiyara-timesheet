# Full API Controllers Plan

> Agreed 7 October 2026. Implementation branch: `feature/full-api-controllers`.

## Progress

- Shared response types, BaseController status mapping, safe exception handler, and validation error mapping implemented.
- Gateway onboarding, Identity onboarding/auth/profile, Notification internal email, Timesheet business/internal snapshot, and Report business routes moved to controllers. Business route handlers now delegate through operation classes where applicable.
- Report Worker and internal HTTP clients updated for the response envelope. Shell and all four remotes unwrap responses, localize safe errors, and the Shell handles a single remote refresh/retry handshake.
- WeatherForecast templates removed. Phase 1–4 smoke scripts and Phase 3/4 HTTP tests updated for envelopes.
- Validation: solution build passed; all 41 .NET tests passed; production builds passed for Shell and all four remotes; `git diff --check` passed. Docker Compose configuration passed. The Phase 5 stack was rebuilt and deployed; all application containers report healthy, `http://localhost:3000/` and gateway health return 200. The separate `aiyara-maildev` stack remains healthy at `http://localhost:8080`; its API token returns 200 and the requested SMTP account authenticated successfully on port 1025. Notifications API is configured to use that SMTP endpoint through `host.docker.internal`.
- The local deployment settings are in the ignored `.env`; no MailDev credentials were added to tracked files. The requested Vue Font Awesome wrapper is installed in the Shell package and its production build passed.
- Remaining before merge: review OpenAPI response metadata and final diff, then record the primary validation summary. Do not merge until these pass.

## Scope

Convert every business HTTP endpoint in Gateway, Identity, Timesheet, Report, and Notification, including `/internal/*`, to ASP.NET Core MVC controllers inheriting `Aiyara.Timesheet.Component.Abstractions.Controllers.BaseController`. Move business work from Minimal API handlers into service or use-case classes. Keep routes, verbs, authorization, tenant boundaries, headers, status codes, and file download behavior. Infrastructure health, OpenAPI/Scalar, YARP proxy, Worker status, and gRPC stay on their current mechanisms. Remove template WeatherForecast endpoints.

## Response Contract

- Use `ReturnResponseWithHttpStatus` from every business controller action.
- Use `ServiceOkResponse<T>` for success and replace `ServiceBadRequestResponse` with `ServiceErrorResponse` for all expected errors.
- `statusCode` is the numeric HTTP status. Errors have a safe `statusMessage`, stable `errorCode`, optional per-field `errors` containing stable codes, and a `traceId` when useful.
- Preserve 201/202 Location headers, 204 without a body, and binary PDF/XLSX downloads through the BaseController helper.
- Override existing `/api/v1` response shapes in place. Update frontend and backend callers of those endpoints together.
- Handle unexpected exceptions globally: log the full exception internally and return a safe 500 envelope with trace ID. Never serialize an Exception or stack trace.

## Frontend

Introduce a shared response and error module for the Shell and four Nuxt remotes. Unwrap success payloads, map error and field codes to Thai and English UI text, show field errors under inputs, show expected errors near the action, and show a safe system error with retry and trace ID. Never show server exception text or raw statusMessage directly. On a remote 401, ask the Shell to refresh once, retry once, then send the user to login if renewal fails.

## Delivery And Validation

1. Change shared response types, BaseController, and global exception handling.
2. Convert each API surface to controllers and extract business operations into services/use cases.
3. Update internal HTTP clients, all frontend callers, OpenAPI/Scalar contracts, and relevant tests.
4. Validate tenant isolation and service ownership, `git diff --check`, affected .NET restore/build/tests, frontend checks and production builds, Compose configuration and changed API paths where feasible, and documentation links.
5. Record primary validation before merging the feature branch into `develop`.

## Related

- [[Timesheet Project Index]]
- [[Target Architecture]]
- [[Security And Tenant Model]]
- [[Development Roadmap]]
