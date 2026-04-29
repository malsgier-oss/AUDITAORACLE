# Installer Validation Profile

This project now supports a reproducible installer validation profile for release-candidate checks.

## Command

```powershell
dotnet build installer\WorkAudit.Installer.wixproj -c Release -p:InstallerValidationProfile=true -p:SkipPublishApp=true
```

## Purpose
- Enables WiX validation by forcing `SuppressValidation=false`.
- Reuses already published app output when `SkipPublishApp=true`.
- Provides a predictable command for release sign-off and CI parity.

## Expected output
- Successful build of `installer/WorkAudit.Installer.wixproj`.
- Validation diagnostics surfaced directly in build output.
- `Audita.msi` generated in installer release output folder.

## Release checklist usage
1. Run normal packaging gates first (`scripts/Verify-QualityGates.ps1`).
2. Run validation profile command above.
3. Capture output and attach to release evidence bundle.

## Current validation snapshot
- Latest run surfaced ICE57 errors in `installer/Package.wxs` for:
  - `StartMenuShortcut`
  - `DesktopShortcut`
- These are now reproducible through the profile command and can be tracked/fixed as release-blocking installer debt.
