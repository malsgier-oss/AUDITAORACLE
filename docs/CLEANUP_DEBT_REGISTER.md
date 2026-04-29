# Cleanup Debt Register

Track cleanup work with explicit risk and test evidence.

| ID | Area | Debt Item | Priority | Risk | Owner | Planned Fix | Test Evidence |
|----|------|-----------|----------|------|-------|-------------|---------------|
| CD-001 | Startup | `App_Startup` still contains login/session orchestration details | High | Medium | Core Team | Move remaining login flow orchestration into dedicated startup/login services | Unit tests for startup decisions + manual login smoke |
| CD-002 | DI | `ServiceContainer` has broad registration surface in one class | High | Medium | Core Team | Continue extracting module-based registrations by domain | Build + service registration diagnostics + integration smoke |
| CD-003 | UI Shell | `MainWindow` still owns many interaction responsibilities | High | Medium | UI Team | Incrementally shift to shell view-model and navigation policy services | Navigation smoke tests + manual tab flow checks |
| CD-004 | Localization | Views directly call localization static APIs in many places | Medium | Low | UI Team | Route future localization through `ILocalizationApplier` | UI text smoke checks in key views |
| CD-005 | Installer | Validation profile not yet wired in CI pipeline | Medium | Low | Build Team | Add validation profile command to release pipeline | `InstallerValidationProfile=true` build evidence |
| CD-006 | Performance Tests | Load test README contains TODO follow-ups | Medium | Medium | QA Team | Convert TODO items into automated smoke load gate | Load smoke command + baseline report |

## Update protocol
- Keep entries current after each cleanup PR.
- Do not close an item without linked test evidence.
