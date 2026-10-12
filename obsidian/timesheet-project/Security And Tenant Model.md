# Security And Tenant Model

> Updated 7 October 2026; Password Handling section refreshed 12 October 2026 against `develop`.

## Authentication And Sessions

- Login uses the seven-character organization code plus email and password. The organization code selects the tenant; email identifies the account within that tenant. UI and reports display a person's first and last name from their profile. See [[Organization Codes And Identity IDs]].
- Identity issues PASETO `v4.public` access tokens signed with Ed25519. Access tokens are short-lived (target: 15 minutes); refresh tokens are opaque, one-time rotating secrets stored only as hashes.
- Token claims include subject, tenant context, role, security/session version, token ID, password-change state, issuer, audience, issued-at, and expiry.
- Gateway and internal APIs validate access tokens through Identity's gRPC API. They cache validation by token ID and session version in Redis for 1–5 minutes. The gRPC response carries the tenant policy version so an in-flight pre-change validation cannot be cached after a policy update. Logout, password reset, account disable, or policy-forced password change publishes an event that revokes the session and invalidates the cache.
- Gateway owns public token handling; only the Identity service holds the private PASETO signing key.

## Accounts And Roles

- Self-registration can create a new tenant or accept a tenant invitation. Joining an existing tenant by typing a company name is forbidden.
- New accounts, password resets, and the bootstrap Platform Admin use a generated temporary password delivered by email. It is valid for 24 hours and one use; issuing a replacement invalidates the old one.
- Activation and reset temporary passwords are stored only as SHA-256 challenge hashes. The user supplies a new policy-compliant password when redeeming one. Identity stores the resulting Argon2id PHC string; Notification stores only the delivery outcome, recipient, and template. MailDev is a local-only inbox.
- The bootstrap Platform Admin account follows the same activation and forced-password-change flow as other accounts. Bootstrap credentials must be supplied through runtime secrets, never committed.
- Platform Admin manages the platform, Tenant Admin manages only its tenant, and Employee manages only their own profile/timesheet data.
- New Identity entity IDs use UUIDv8 from `Aiyara.Timesheet.Component.Abstractions`; the generator receives cryptographically random region and shard IDs in the inclusive range 1–9. The fixed Platform Admin tenant ID is a reserved bootstrap ID, not a generated ID.

## Password Handling

- `EncryptionService` (`Aiyara.Timesheet.Component.Abstractions.Securities`) implements this section: Argon2id via Konscious.Security.Cryptography (19 MiB memory, 2 iterations, parallelism 1), parameter-aware verification that reads the embedded `m=`/`t=`/`p=` values from the PHC string rather than assuming fixed ones, `CryptographicOperations.FixedTimeEquals` for constant-time comparison, and `RandomNumberGenerator` for temporary-password generation. This replaced the earlier PBKDF2 implementation; there is nothing further to do here.
- Store one PHC-format value in `password_hash`; the value contains algorithm, parameters, salt, and hash. Do not have a separate salt column and never log plaintext or temporary passwords.
- Generate temporary passwords with `RandomNumberGenerator` and validate them with the same password-policy engine.
- An Account has one credential across tenant memberships. Reset or activation increments the Account session version for every tenant; old refresh tokens in any tenant cannot rotate, and Identity publishes a revocation version for every membership's tenant.
- Existing password compliance cannot be recovered from an Argon2id hash. A stricter tenant policy therefore conservatively forces existing members to reset; relaxing a policy does not. A forced-change token cannot authorize tenant administration or protected API calls.
- Password policy is configured per tenant by Tenant Admin: minimum length and lower-case, upper-case, numeric, and symbol requirements. New passwords comply immediately; when a stricter policy makes an existing password noncompliant, mark the account `must_change_password` and require a change on the next login.
- Rate-limit sign-in and secret-request endpoints by IP and a hashed tenant/account key in Redis; store only hashed activation, invitation, refresh, and reset secrets.

## Data Protection And Audit

- Enforce tenant isolation via service-level authorization and database query filters. The client must never be trusted to choose a tenant.
- Time entries use soft delete and audit events (actor, timestamp, before/after values, and reason when supplied). Users may create, edit, or delete only their own entries in the current tenant-local month.
- Closed-month entries are immutable. Reports read immutable snapshots, not live rows.
- Tenant-configurable retention defaults to seven years for timesheets, audit logs, reports, and signed documents.
- Redact passwords, tokens, SMTP credentials, and secrets from logs, traces, errors, API examples, and documentation.

## Related

- [[Target Architecture]]
- [[Development Roadmap]]
- [[Services And Projects]]
