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

## Parallel Agents

At most three agents work at the same time. The primary agent spawns them and keeps count; a fourth waits for one to finish.

- Each implementation agent works on its own Git Flow branch in its own worktree (spawn it with `isolation: "worktree"`), so two agents never share a working tree or a checked-out branch.
- Parallel branches must have disjoint surfaces. Backend work that adds an EF migration is not run alongside another migration branch of the same service: they share the model snapshot, so the second starts after the first merges into `develop`.
- A backend change and its frontend half stay in sequence on one branch: the frontend agent starts from the backend hand-back.
- The Docker Compose stack, its ports, and `tests/phase3/Smoke.ps1` exist once per machine. Only one agent builds the stack or runs the smoke test at a time; the primary agent hands that slot out.
- `docs-keeper` edits only `obsidian/` and may run beside an implementer on the same branch once the implementation is committed.
- Merges into `develop` are made by the primary agent one at a time, each after its own validation; the next branch rebases or merges the latest `develop` first.
