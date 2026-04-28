# WorkAudit Windows Installer (MSI)

## Deliverable

- **MSI** (`Audita.msi`) — standard Windows Installer package. Copy this file to another PC and run it (Administrator). This is what you distribute for a pilot.
- **MST** — an optional **transform** file applied *on top of* an MSI (`msiexec /i Audita.msi TRANSFORMS=custom.mst`). IT teams use MSTs to tweak install options without rebuilding the MSI. This repo does **not** generate an MST by default; build the MSI first, then create transforms with Orca / WiX / your packaging tool if needed.

## Build the MSI

Prerequisites: [.NET SDK](https://dotnet.microsoft.com/download) (8.x). WiX is restored automatically via NuGet (`WixToolset.Sdk`).

From the repository root:

```powershell
.\scripts\Build-WorkAuditMsi.ps1
```

Or:

```powershell
dotnet build .\installer\WorkAudit.Installer.wixproj -c Release
```

Output: `installer\bin\Release\Audita.msi`

**Administrator / UAC (important on Windows 11):** The short right‑click menu often **does not list “Run as administrator”** for `.msi` files. Use one of these:

1. **Easiest:** Double‑click **`Install-WorkAudit-Elevated.cmd`** (next to `Audita.msi` after you build the installer). It shows the normal UAC prompt, then starts the MSI.
2. Right‑click **`Audita.msi`** → **Show more options** (bottom of the menu) *or* **Shift+right‑click** the MSI → in the **classic** menu choose **Run as administrator**.
3. Or double‑click **`Install-WorkAudit-Elevated.ps1`** and choose **Run with PowerShell** (if prompted).

You can also run from an **elevated** Command Prompt: `msiexec /i "full\path\Audita.msi"`

### Configure data paths during install

The MSI supports two public properties:

- `WORKAUDIT_BASE_DIR` - folder for imported documents and default app data
- `WORKAUDIT_ORACLE_CONNECTION` - Oracle connection string (ODP.NET format)

Interactive install with explicit values:

```powershell
msiexec /i ".\installer\bin\Release\Audita.msi" `
  WORKAUDIT_BASE_DIR="D:\WorkAuditData\Documents" `
  WORKAUDIT_ORACLE_CONNECTION="User Id=workaudit;Password=change-me;Data Source=//localhost:1521/FREEPDB1"
```

Silent install (no UI) with logging:

```powershell
msiexec /i ".\installer\bin\Release\Audita.msi" /qn /l*v ".\workaudit-install.log" `
  WORKAUDIT_BASE_DIR="D:\WorkAuditData\Documents" `
  WORKAUDIT_ORACLE_CONNECTION="User Id=workaudit;Password=change-me;Data Source=//localhost:1521/FREEPDB1"
```

These values are written as machine environment variables (`WORKAUDIT_BASE_DIR` and `WORKAUDIT_ORACLE_CONNECTION`) so WorkAudit uses them on first launch.

The installer project **publishes** the app self-contained (`-r win-x64 -p:WorkAuditPortable=true`) into `artifacts\msi-app\`, then harvests that folder into the MSI. To reuse an existing publish without rebuilding:

```powershell
dotnet build .\installer\WorkAudit.Installer.wixproj -c Release -p:SkipPublishApp=true
```

### Bundled WebView2 (optional)

If you ship the **fixed** WebView2 runtime, expand it under `artifacts\msi-app\WebView2Runtime\` (see `scripts\Expand-WebView2FixedRuntime.ps1`) **before** building the MSI so those files are included.

### Version and upgrades

1. **Keep the same `UpgradeCode`** in `installer\Package.wxs` for all releases of this product line (already set).
2. **Single version file:** `build\WorkAudit.Version.props` drives the app (`WorkAudit.csproj` imports it), assembly versions, and the **MSI ProductVersion** via `PackageVersion=$(Version)` in the WiX project. The **`InstallerVersion="500"`** attribute on `Package` in `Package.wxs` is the *minimum Windows Installer engine* required — it is **not** the app version. **Do not** duplicate version numbers in multiple unrelated places.
3. **Continuous upgrades (same version number):** `installer\Package.wxs` enables `AllowSameVersionUpgrades="yes"` on `MajorUpgrade`, so installing a newly-built `Audita.msi` will **upgrade in place** even when `build\WorkAudit.Version.props` has the same `<Version>` as what is already installed.
4. **Important caveat:** Windows Installer compares only the first three version fields (major.minor.build). With `AllowSameVersionUpgrades="yes"`, two MSIs that share the same first three fields can upgrade each other because MSI treats them as the “same version”. This is convenient for continuous rebuilds, but it also means you can accidentally replace a newer build with an older one if you redistribute an out-of-date MSI. Keep your release pipeline linear, and consider bumping `<Version>` for externally distributed releases.
5. Use `.\scripts\Bump-VersionAndRebuildMsi.ps1` when you want a normal “newer version” release (for example, `1.0.0` → `1.0.1`) and to keep `AssemblyVersion` / `FileVersion` aligned with your release policy.
6. When you need a **new MSI** (same code or new code — installers must be rebuilt to pick up changes):

```powershell
# Bump patch (2.0.0 -> 2.0.1) and rebuild MSI — use for upgrade installs
.\scripts\Bump-VersionAndRebuildMsi.ps1

# Or edit build\WorkAudit.Version.props by hand (bump Version + AssemblyVersion + FileVersion), then:
.\scripts\Build-WorkAuditMsi.ps1
```

After a build, you can confirm the MSI matches `WorkAudit.Version.props`:

```powershell
.\scripts\Verify-WorkAuditMsiVersion.ps1
```

Installing a newer MSI on a machine that already has AUDITA performs a **major upgrade** (see `MajorUpgrade` in `Package.wxs`). Downgrades are blocked unless you change that authoring.

### Signing (production)

Sign `Audita.msi` with an **Authenticode** (code signing) certificate. Until you do, Windows may show **Smart App Control** / **SmartScreen** warnings or block the MSI because the publisher cannot be verified — **setting `Manufacturer="CertisTech.com"` in WiX does not replace a digital signature.**

Example (after you have a `.pfx`):

```powershell
signtool sign /fd SHA256 /f YourCert.pfx /p YourPassword /tr http://timestamp.digicert.com /td SHA256 .\installer\bin\Release\Audita.msi
```

### Troubleshooting: “Smart App Control blocked” or “policies prevent this installation”

You may see **two** separate issues:

1. **Smart App Control / unknown publisher** — The MSI is **unsigned**. For production, **sign the MSI** (above). For **testing on your own PC only**, you can temporarily relax protection: **Settings → Privacy & security → Windows Security → App & browser control → Smart App Control** — set to **Off** (or use **Evaluation** mode). Reboot if required. This is not suitable for locked-down corporate PCs; there you need a signed installer or an IT allow rule.

2. **“The system administrator has set policies to prevent this installation”** — Usually **Group Policy** or corporate policy is blocking MSI installs for standard users or blocking unlisted apps. Fixes are **IT-side**: adjust **Windows Installer** / **Software Restriction** policies, or install from an account that is allowed to install, or use a **signed** package that meets your org’s policy. On a standalone machine, check `gpedit.msc` → **Computer Configuration → Administrative Templates → Windows Components → Windows Installer** (e.g. “Turn off Windows Installer” should be **Not configured** or **Disabled**).

**Note:** Unblocking the file in **Properties → General** only helps with the “came from the internet” flag; it does **not** replace signing for Smart App Control.

### Legacy files

`ApplicationFiles.wxs`, `Product.wxs`, `WorkAudit.wxs`, and `Components.wxs` in this folder are **old** WiX v3 / draft sources. The active project compiles **`Package.wxs`** only (`EnableDefaultCompileItems=false`).
