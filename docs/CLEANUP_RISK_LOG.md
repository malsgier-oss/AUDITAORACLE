# Cleanup Risk Log

Use this log during cleanup/polish work to make each refactor auditable and reversible.

## How to use
- Create one row per change batch/PR.
- Keep rollback instructions concrete and executable.
- Link each risk to validation evidence (build, tests, manual checks).

## Template

| Date | Area | Change Summary | Risk Level | Potential Impact | Rollback Plan | Validation Evidence | Owner | Status |
|------|------|----------------|------------|------------------|---------------|---------------------|-------|--------|
| YYYY-MM-DD | Startup / UI / Storage / Installer | Short description | Low / Medium / High | What can fail for users | Exact steps to revert safely | Commands, logs, or test names | Name | Planned / In Progress / Done |

## Example

| Date | Area | Change Summary | Risk Level | Potential Impact | Rollback Plan | Validation Evidence | Owner | Status |
|------|------|----------------|------------|------------------|---------------|---------------------|-------|--------|
| 2026-04-29 | Startup | Extracted startup orchestration from `App_Startup` to coordinator service | Medium | Incorrect startup order can break login flow | Revert commit and run quality gates script | `scripts/Verify-QualityGates.ps1` + manual login smoke test | Dev Team | Done |
