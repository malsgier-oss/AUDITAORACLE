# Localization Implementation Guide

This document provides guidelines for implementing full Arabic/English localization in WorkAudit.

## Overview

WorkAudit currently supports bilingual reports but needs full UI localization.

## Resource Files Structure

Create two resource files:

### 1. Resources/Strings.resx (English - Default)
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="AppTitle" xml:space="preserve">
    <value>WorkAudit - Document Management System</value>
  </data>
  <data name="ImportSuccess" xml:space="preserve">
    <value>Document imported successfully</value>
  </data>
  <data name="ImportError" xml:space="preserve">
    <value>Error importing document: {0}</value>
  </data>
  <!-- Add 200+ more strings -->
</root>
```

### 2. Resources/Strings.ar.resx (Arabic)
```xml
<?xml version="1.0" encoding="utf-8"?>
<root>
  <data name="AppTitle" xml:space="preserve">
    <value>WorkAudit - نظام إدارة الوثائق</value>
  </data>
  <data name="ImportSuccess" xml:space="preserve">
    <value>تم استيراد الوثيقة بنجاح</value>
  </data>
  <data name="ImportError" xml:space="preserve">
    <value>خطأ في استيراد الوثيقة: {0}</value>
  </data>
  <!-- Add 200+ more strings in Arabic -->
</root>
```

## Implementation Steps

### Step 1: Create Resource Files

1. Right-click on project → Add → New Folder → "Resources"
2. Right-click Resources folder → Add → New Item → Resources File
3. Name it "Strings.resx"
4. Set Build Action to "Embedded Resource"
5. Set Custom Tool to "PublicResXFileCodeGenerator"
6. Repeat for "Strings.ar.resx"

### Step 2: Extract Hardcoded Strings

#### Before
```csharp
MessageBox.Show("Document imported successfully", "Success", 
    MessageBoxButton.OK, MessageBoxImage.Information);
```

#### After
```csharp
MessageBox.Show(Resources.Strings.ImportSuccess, Resources.Strings.Success, 
    MessageBoxButton.OK, MessageBoxImage.Information);
```

### Step 3: XAML Binding

#### Before
```xml
<Button Content="Import" Click="Import_Click" />
```

#### After
```xml
<Button Content="{x:Static res:Strings.ButtonImport}" Click="Import_Click"
        xmlns:res="clr-namespace:WorkAudit.Resources" />
```

### Step 4: RTL Layout Support

Add FlowDirection binding:

```csharp
// In App.xaml.cs
public static FlowDirection CurrentFlowDirection => 
    Thread.CurrentThread.CurrentUICulture.TextInfo.IsRightToLeft 
        ? FlowDirection.RightToLeft 
        : FlowDirection.LeftToRight;
```

```xml
<!-- In each Window/Page -->
<Window FlowDirection="{x:Static local:App.CurrentFlowDirection}">
```

## Files Requiring Localization

### Priority 1: User-Facing Messages (50+ files)
- All MessageBox.Show() calls
- Status bar messages
- Toast notifications
- Error messages
- Validation messages

### Priority 2: UI Labels (20+ XAML files)
- Button Content
- Label Content
- Menu items
- Tab headers
- GroupBox headers

### Priority 3: Reports (5 files)
- Already implemented in ExecutiveSummaryReport.cs
- Verify all other reports support bilingual output

### Priority 4: Configuration (2 files)
- Default document types
- Default branches
- System messages

## String Categories

Organize strings by category:

```
Common.AppTitle
Common.OK
Common.Cancel
Common.Save
Common.Delete
Common.Search

Import.Title
Import.Success
Import.Error
Import.DuplicateWarning

Workspace.Title
Workspace.FilterBy
Workspace.SortBy

Error.FileNotFound
Error.AccessDenied
Error.InvalidFormat
Error.NetworkError

Validation.Required
Validation.InvalidEmail
Validation.InvalidDate
```

## Language Switching

Add language switcher in settings:

```csharp
public void SetLanguage(string cultureName)
{
    var culture = new CultureInfo(cultureName);
    Thread.CurrentThread.CurrentCulture = culture;
    Thread.CurrentThread.CurrentUICulture = culture;
    
    // Save preference
    UserSettings.Set("language", cultureName);
    
    // Restart required for full effect
    MessageBox.Show(Resources.Strings.RestartRequired);
}
```

## Testing Localization

### Test Matrix

| Test | Arabic | English |
|------|--------|---------|
| UI renders correctly | ✓ | ✓ |
| Text is readable | ✓ | ✓ |
| No truncation | ✓ | ✓ |
| RTL layout works | ✓ | N/A |
| Date/Time formats | ✓ | ✓ |
| Number formats | ✓ | ✓ |
| Currency formats | ✓ | ✓ |

### Testing Checklist

- [ ] All UI text uses resource strings
- [ ] No hardcoded English strings remain
- [ ] Arabic text displays correctly
- [ ] RTL layout doesn't break UI
- [ ] Date/time shows in user's locale
- [ ] Numbers format correctly
- [ ] Reports support both languages
- [ ] Error messages are localized
- [ ] Validation messages are localized

## Implementation Estimate

- Resource file creation: 4-6 hours
- String extraction (50+ files): 15-20 hours
- Arabic translation: 8-10 hours (native speaker)
- RTL layout fixes: 4-6 hours
- Testing: 4-6 hours

**Total**: 35-48 hours

## Example: Complete File Conversion

### Before (hardcoded)
```csharp
public class ImportView
{
    private void ShowSuccess()
    {
        StatusText.Text = "Import completed successfully";
    }
    
    private void ShowError(string error)
    {
        MessageBox.Show($"Import failed: {error}", "Error");
    }
}
```

### After (localized)
```csharp
using WorkAudit.Resources;

public class ImportView
{
    private void ShowSuccess()
    {
        StatusText.Text = Strings.ImportCompletedSuccessfully;
    }
    
    private void ShowError(string error)
    {
        MessageBox.Show(
            string.Format(Strings.ImportFailed, error), 
            Strings.Error);
    }
}
```

## References

- [WPF Localization](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/advanced/wpf-globalization-and-localization-overview)
- [Resource Files](https://docs.microsoft.com/en-us/dotnet/framework/resources/creating-resource-files-for-desktop-apps)
- [RTL Support in WPF](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/advanced/bidirectional-features-in-wpf-overview)
