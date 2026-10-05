# Development Workflow

## Git Flow

Use Git Flow branches for every implementation task.

- Start feature and fix work from the latest `develop` branch.
- Use `feature/<short-topic>` for planned work and `fix/<short-topic>` for defects.
- Use `release/<version>` only to stabilize a release from `develop`.
- Use `hotfix/<short-topic>` only for an urgent correction from `main`; merge it back to both `main` and `develop`.
- Keep each branch limited to one coherent change. Rebase or merge the latest `develop` before requesting validation when conflicts or drift exist.
- Do not commit directly to `develop` or `main`.

## Validation Gate For `develop`

The primary agent validates every change before it is merged into `develop`. The implementation agent supplies the branch name, change summary, and exact validation commands/results; the primary agent inspects the diff and independently runs the relevant checks.

Validation is complete only when all applicable checks below pass:

1. Scope and dependency review: the diff matches the requested work, preserves tenant isolation and service ownership, and contains no secrets or generated build artifacts.
2. Static review: `git diff --check` passes and no unresolved conflict markers remain.
3. Backend changes: restore/build the affected .NET projects and run affected unit/integration/contract tests.
4. Frontend changes: run the affected frontend type/lint checks when configured and a production build.
5. Runtime/configuration changes: validate Docker Compose configuration and exercise changed health/API paths where feasible.
6. API, database, or message-contract changes: update OpenAPI/Scalar, migrations, contracts, and their tests in the same branch.
7. Documentation changes: update affected project/Obsidian documentation and validate its links.

Merge into `develop` only after the primary agent records a passing validation summary. Failures are corrected on the same Git Flow branch and revalidated.
