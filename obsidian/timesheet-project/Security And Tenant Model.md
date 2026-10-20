# Security And Tenant Model

> Updated 7 October 2026; Password Handling section refreshed 12 October 2026 against `develop`.

## Authentication And Sessions

- Sign-in needs only email and password. Accounts are global (email is unique), so Identity finds the account's active memberships in active organizations: one membership issues a session immediately; several return the organization list (only after the password is verified) and the user picks one, which logs in with that organization code. Login attempts are limited per email across organizations. System administrators use `/sys-admin/login`, which accepts only `PlatformAdmin` memberships. UI and reports display a person's first and last name from their profile. See [[Organization Codes And Identity IDs]].
- Identity issues PASETO `v4.public` access tokens signed with Ed25519. Access tokens are short-lived (target: 15 minutes); refresh tokens are opaque, one-time rotating secrets stored only as hashes.
- Token claims include subject, tenant context, role, security/session version, token ID, password-change state, issuer, audience, issued-at, and expiry.
- Gateway and internal APIs validate access tokens through Identity's gRPC API. They cache validation by token ID and session version in Redis for 1–5 minutes. The gRPC response carries the tenant policy version so an in-flight pre-change validation cannot be cached after a policy update. Logout, password reset, account disable, or policy-forced password change publishes an event that revokes the session and invalidates the cache.
- Gateway owns public token handling; only the Identity service holds the private PASETO signing key.
- Platform Admin sign-in needs a second factor: a TOTP code from an enrolled authenticator, or one single-use recovery code. No route issues a Platform Admin session without it. Authenticator secrets are encrypted with AES-256-GCM under `MFA_ENCRYPTION_KEY`, which is separate from the signing key; recovery codes are stored only as hashes. Five wrong second factors lock the account for 15 minutes. Only another Platform Admin can reset an admin's MFA. The platform tenant's password policy is managed by Platform Admins in the SysAdmin console.

## Accounts And Roles

- Self-registration can create a new tenant or accept a tenant invitation. Joining an existing tenant by typing a company name is forbidden.
- New accounts, password resets, and the bootstrap Platform Admin use a generated temporary password delivered by email. It is valid for 24 hours and one use; issuing a replacement invalidates the old one.
- Forgot password (`POST /api/v1/auth/password/forgot`, Shell `/forgot-password`) takes only the email, because the password belongs to the account rather than one organization. It always answers 202, attempts are limited per email like login, and nothing is sent for an unknown, unactivated, or suspended-only account. The new password, set at `/reset-password`, must satisfy the policy of every active organization the account belongs to; the reset clears every such membership's forced-change flag and ends all of the account's sessions. A Platform Admin's MFA is not affected by a password reset.
- Activation and reset temporary passwords are stored only as SHA-256 challenge hashes. The user supplies a new policy-compliant password when redeeming one. Identity stores the resulting Argon2id PHC string; Notification stores only the delivery outcome, recipient, and template. MailDev is a local-only inbox.
- The bootstrap Platform Admin account follows the same activation and forced-password-change flow as other accounts. Bootstrap credentials must be supplied through runtime secrets, never committed.
- Platform Admin manages the platform, Tenant Admin manages only its tenant, and Employee manages only their own profile/timesheet data. Timesheet and Report grant tenant-admin rights (catalog, month lock, every member's reports) only to `TenantAdmin`; a `PlatformAdmin` token is scoped to the platform tenant and is treated like any non-admin there. The Shell shows Platform Admins only Profile and Organizations, shows Administration only to Tenant Admins, and answers a direct `/remote/<id>` visit by a role without access with "Access restricted" instead of loading the remote. Platform Admin accounts are dedicated: Identity rejects using a Platform Admin's email to create, invite into, or accept an invitation to a customer organization, and refuses a bootstrap email that already belongs to one. Every SysAdmin mutation, including organization creation, goes through an audited `/api/v1/platform/**` route that Gateway rate-limits. Platform Admins are managed from the roster: invited admins activate like the bootstrap admin, a deactivated membership cannot sign in, refresh, or activate, and no admin can deactivate themselves, so one always remains. A Platform Admin can demote or remove a tenant administrator but never the organization's last active one. Every such change bumps the account's session version, so its sessions end everywhere.
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
