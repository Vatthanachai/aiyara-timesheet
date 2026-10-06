# Security And Tenant Model

> Agreed design, 5 October 2026.

## Authentication And Sessions

- Login uses email only. UI and reports display a person's first and last name from their profile.
- Identity issues PASETO `v4.public` access tokens signed with Ed25519. Access tokens are short-lived (target: 15 minutes); refresh tokens are opaque, one-time rotating secrets stored only as hashes.
- Token claims include subject, tenant context, role, security/session version, token ID, password-change state, issuer, audience, issued-at, and expiry.
- Gateway and internal APIs validate access tokens through Identity's gRPC API. They cache validation by token ID and session version in Redis for 1–5 minutes. Logout, password reset, account disable, or policy-forced password change publishes an event that revokes the session and invalidates the cache.
- Gateway owns public token handling; only the Identity service holds the private PASETO signing key.

## Accounts And Roles

- Self-registration can create a new tenant or accept a tenant invitation. Joining an existing tenant by typing a company name is forbidden.
- New accounts, password resets, and the bootstrap Platform Admin use a generated temporary password delivered by email. It is valid for 24 hours and one use; issuing a replacement invalidates the old one.
- Activation and reset temporary passwords are stored only as SHA-256 challenge hashes. The user supplies a new policy-compliant password when redeeming one. Identity stores the resulting Argon2id PHC string; Notification stores only the delivery outcome, recipient, and template. MailDev is a local-only inbox.
- The bootstrap Platform Admin account follows the same activation and forced-password-change flow as other accounts. Bootstrap credentials must be supplied through runtime secrets, never committed.
- Platform Admin manages the platform, Tenant Admin manages only its tenant, and Employee manages only their own profile/timesheet data.

## Password Handling

- Replace the existing `EncryptionService` password implementation. It is not suitable for the target design: it uses PBKDF2 rather than Argon2id, does not verify using the embedded iteration value, does not compare in constant time, and uses non-cryptographic randomness when generating passwords.
- Use Argon2id and store one PHC-format value in `password_hash`; the value contains algorithm, parameters, salt, and hash. Do not have a separate salt column and never log plaintext or temporary passwords.
- Generate temporary passwords with `RandomNumberGenerator` and validate them with the same password-policy engine.
- An Account has one credential across tenant memberships. Reset or activation increments the Account session version for every tenant; old refresh tokens in any tenant cannot rotate, and Identity publishes a revocation version for every membership's tenant.
- Existing password compliance cannot be recovered from an Argon2id hash. A stricter tenant policy therefore conservatively forces existing members to reset; relaxing a policy does not. A forced-change token cannot authorize tenant administration or protected API calls.
- Password policy is configured per tenant by Tenant Admin: minimum length and lower-case, upper-case, numeric, and symbol requirements. New passwords comply immediately; when a stricter policy makes an existing password noncompliant, mark the account `must_change_password` and require a change on the next login.
- Rate-limit sign-in and secret-request endpoints by account and IP; store only hashed activation, invitation, refresh, and reset secrets.

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
