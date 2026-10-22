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
5. Runtime/configuration changes: validate Docker Compose configuration (`docker-compose.yml` and `docker-compose.test.yml`) and exercise changed health/API paths on a test stack (`pwsh -NoProfile -File scripts/test-stack.ps1 up`, then `smoke`), never on the main stack.
6. API, database, or message-contract changes: update OpenAPI/Scalar, migrations, contracts, and their tests in the same branch.
7. Documentation changes: update affected project/Obsidian documentation and validate its links.

Merge into `develop` only after the primary agent records a passing validation summary. Failures are corrected on the same Git Flow branch and revalidated.

## Parallel Agents

Every role (backend-implementer, frontend-implementer, docs-keeper, validator) may run at the same time, so work flows as a pipeline: while the validator checks one branch, the implementers start the next. The primary agent spawns them, tracks which branch each one holds, and hands out the shared slots below.

- Each agent works on its own Git Flow branch in its own worktree (spawn it with `isolation: "worktree"`), so no two agents share a working tree or a checked-out branch. Only the primary agent works in the main checkout.
- A backend change and its frontend half stay in sequence on one branch: the frontend agent starts from the backend hand-back. Different changes run side by side.
- Two branches may each add an EF migration to the same service. The one that merges second first merges the latest `develop`, deletes its own migration, regenerates it on top of the merged snapshot, and reruns its tests before validation.
- Runtime checks run on a test stack, not on the user's main Compose stack. Each agent may start its own with `scripts/test-stack.ps1` (`docker-compose.test.yml`: its own project name, network, volumes, database, and loopback ports from `TEST_STACK_NAME` and `TEST_PORT_BASE`, derived from the worktree path unless set), run `tests/phaseN/Smoke.ps1` against it with `test-stack.ps1 smoke -Phase N`, and tear it down with `down` or `reset`. Implementers may do this on their own branch; it never touches the main stack.
- The main compose (`docker-compose.yml`, project `aiyara-timesheet`) is rebuilt only by the primary agent, only after a change is validated and merged into `develop`, with `scripts/deploy-main.ps1` (it refuses unless run in the main checkout on a clean `develop`). No other agent restarts, rebuilds, or modifies the main stack or its volumes.
- `docs-keeper` edits only `obsidian/` and may run beside an implementer once the implementation is committed.
- Merges into `develop` are made by the primary agent one at a time, each after its own validation; the next branch merges the latest `develop` first.
