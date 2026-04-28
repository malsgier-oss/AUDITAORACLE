# UI Automation Tests Setup Guide

This document explains the UI automation testing infrastructure for WorkAudit using FlaUI.

## Overview

FlaUI is a .NET library for automated testing of Windows application UIs. It uses the UI Automation framework provided by Microsoft to interact with WPF applications.

## Prerequisites

- FlaUI.Core (v4.0.0)
- FlaUI.UIA3 (v4.0.0) - UIA3 automation provider
- WorkAudit application must be built in Release or Debug mode
- Application executable path: `bin\Release\net8.0-windows\WorkAudit.exe`

## Test Categories

### 1. Authentication & User Management
- Login with valid credentials
- Login with invalid credentials
- Logout functionality
- User profile access

### 2. Document Management
- Document import workflow
- Document classification
- Document search and filter
- Document details view
- Document status update

### 3. Report Builder
- Create new report template
- Select fields for report
- Add filters to report
- Generate report
- Export report to Excel/PDF

### 4. Navigation
- Menu navigation
- View switching
- Dashboard access
- Settings access

### 5. Data Entry
- Form field validation
- Required field handling
- Data persistence
- Error message display

## Implementation Notes

UI automation tests require:
1. **Application Launch**: Tests must start the application programmatically
2. **Element Identification**: UI elements need stable identifiers (AutomationId, Name, ClassName)
3. **Synchronization**: Proper waits for UI elements to load
4. **Cleanup**: Ensure application closes after each test

## Current Limitations

- FlaUI tests require the application to have UI AutomationIds set on controls
- Tests must run on Windows with UI Automation enabled
- Tests cannot run in headless/CI environments without special configuration
- Slower execution compared to unit/integration tests

## Recommendations for Full Implementation

1. **Add AutomationId to all WPF controls** in XAML:
   ```xml
   <Button x:Name="btnLogin" AutomationProperties.AutomationId="LoginButton" />
   ```

2. **Create test fixtures** that handle app lifecycle
3. **Use Page Object pattern** to encapsulate UI interactions
4. **Implement retry logic** for flaky UI interactions
5. **Run on dedicated test machines** to avoid interference

## Example Test Structure

```csharp
public class LoginFlowTests : IDisposable
{
    private Application _app;
    private Window _mainWindow;

    public LoginFlowTests()
    {
        var appPath = Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..\..\..\bin\Release\net8.0-windows\WorkAudit.exe");
        _app = Application.Launch(appPath);
        _mainWindow = _app.GetMainWindow(new UIA3Automation());
    }

    [Fact]
    public void Login_WithValidCredentials_ShouldSucceed()
    {
        // Arrange
        var usernameBox = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UsernameTextBox")).AsTextBox();
        var passwordBox = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("PasswordBox")).AsTextBox();
        var loginButton = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("LoginButton")).AsButton();

        // Act
        usernameBox.Enter("admin");
        passwordBox.Enter("password");
        loginButton.Click();

        // Assert
        Wait.UntilInputIsProcessed();
        var dashboard = _mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DashboardView"));
        dashboard.Should().NotBeNull();
    }

    public void Dispose()
    {
        _app?.Close();
        _app?.Dispose();
    }
}
```

## Next Steps

To fully implement UI automation tests:

1. Review all WPF views and add `AutomationProperties.AutomationId` to controls
2. Create `UITestBase` class for common setup/teardown
3. Implement Page Object classes for each major view
4. Write test scenarios for critical user workflows
5. Integrate with CI/CD pipeline (optional, requires special setup)

## Status

**Note**: Full UI automation tests are not implemented due to:
- Missing AutomationId properties on most WPF controls
- Complexity of setting up reliable UI test infrastructure
- Time constraints

The test infrastructure (FlaUI packages) has been added to the project and is ready for implementation when needed.
