# How to Install WorkAudit on Other Devices

Use one of these two methods to install WorkAudit on other PCs (e.g. bank branches).

---

## Method 1: ZIP Package (Simplest – Recommended)

### On your development machine (one time)

1. **Create the deployment package**
   ```powershell
   cd D:\AuditaProject\WorkAudit.CSharp
   .\scripts\Create-DeploymentPackage.ps1 -Version "1.0.0"
   ```
   This creates: `WorkAudit-v1.0.0-Deployment.zip` in the project folder.

2. **Copy the ZIP to the target device**
   - USB drive, network share, or secure transfer.

### On each target device

1. **Install .NET 8.0 Runtime** (if not already installed)
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0  
   - Install **.NET Desktop Runtime 8.0** (not SDK).

2. **Extract the ZIP**
   - Right-click `WorkAudit-v1.0.0-Deployment.zip` → **Extract All**  
   - Choose a folder (e.g. `C:\Temp\WorkAudit`).

3. **Run the installer**
   - Open the extracted folder.
   - Right-click **`install.bat`** → **Run as administrator**.
   - Accept the prompts. The app is installed to `C:\Program Files\WorkAudit`.

4. **Launch WorkAudit**
   - Use the Desktop or Start Menu shortcut.
   - On first launch, note the temporary admin password and change it in Settings.

---

## Method 2: MSI Installer (WiX)

Use this if you want a standard Windows installer (e.g. for group policy or silent install).

### On your development machine (one time)

1. **Install WiX Toolset**  
   https://wixtoolset.org/

2. **Build the app and MSI**
   ```powershell
   cd D:\AuditaProject\WorkAudit.CSharp
   dotnet publish WorkAudit.csproj -c Release -r win-x64 --self-contained false -o ".\bin\Release\net8.0-windows"
   cd installer
   wix build -arch x64 -o ..\bin\Release\WorkAudit-Setup.msi WorkAudit.wxs
   ```
   Output: `bin\Release\WorkAudit-Setup.msi`.

3. **(Optional) Sign the MSI** (recommended for production)
   ```powershell
   signtool sign /f YourCertificate.pfx /p CertPassword /t http://timestamp.digicert.com bin\Release\WorkAudit-Setup.msi
   ```

4. **Copy the MSI** to each target device (USB, network, etc.).

### On each target device

1. **Install .NET 8.0 Desktop Runtime** if needed (see Method 1).

2. **Run the MSI**
   - Double-click `WorkAudit-Setup.msi`  
   - Or right-click → **Run as administrator**.
   - Follow the wizard.

3. **Launch WorkAudit** from Desktop or Start Menu and change the default admin password on first run.

**Silent install (for IT rollout):**
```powershell
msiexec /i WorkAudit-Setup.msi /quiet /norestart /l*v install.log
```

---

## Requirements on each device

- **OS:** Windows 10 (1809+) or Windows 11, 64-bit  
- **.NET:** .NET 8.0 Desktop Runtime  
- **RAM:** 4 GB minimum, 8 GB recommended  
- **Disk:** 10 GB free minimum, 50 GB recommended for data  
- **Rights:** Administrator to install; normal users can run after install  

---

## Optional: Pre-configure per branch

Before first launch on a branch PC, you can set environment variables (as Administrator):

```powershell
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_BASE_DIR", "D:\WorkAuditData", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_BRANCH", "Branch01", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_USERNAME", "admin", "Machine")
[System.Environment]::SetEnvironmentVariable("WORKAUDIT_ADMIN_EMAIL", "admin@branch01.bank.com", "Machine")
```

Then restart the PC or log off and back on before starting WorkAudit.

---

## Quick summary

| Step | Action |
|------|--------|
| 1 | On your PC: run `.\scripts\Create-DeploymentPackage.ps1 -Version "1.0.0"` |
| 2 | Copy `WorkAudit-v1.0.0-Deployment.zip` to the other device |
| 3 | On the other device: install .NET 8.0 Desktop Runtime if needed |
| 4 | Extract the ZIP, then right-click `install.bat` → **Run as administrator** |
| 5 | Open WorkAudit from Desktop/Start Menu and change the admin password |

User data (database, logs, config) is stored in `%APPDATA%\WORKAUDIT` and is kept when you uninstall.
