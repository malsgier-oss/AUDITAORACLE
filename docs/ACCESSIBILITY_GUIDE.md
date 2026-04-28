# Accessibility Implementation Guide

This document provides guidelines and examples for adding accessibility features to WorkAudit.

## AutomationProperties

AutomationProperties make UI elements accessible to screen readers and assistive technologies.

### Required Properties

1. **AutomationProperties.Name** - Short, descriptive name of the element
2. **AutomationProperties.HelpText** - Detailed description of element's purpose
3. **AutomationProperties.LabeledBy** - Associates label with input control
4. **AutomationProperties.AutomationId** - Unique identifier for automation testing

### Implementation Examples

#### Buttons
```xml
<!-- Before -->
<Button Content="Import" Click="Import_Click" />

<!-- After -->
<Button Content="Import" 
        Click="Import_Click"
        AutomationProperties.Name="Import Documents"
        AutomationProperties.HelpText="Import documents from files or webcam"
        AutomationProperties.AutomationId="btnImport" />
```

#### TextBoxes with Labels
```xml
<!-- Before -->
<Label Content="Document Number:" />
<TextBox x:Name="txtDocNumber" />

<!-- After -->
<Label x:Name="lblDocNumber" 
       Content="Document Number:"
       AutomationProperties.Name="Document Number Label" />
<TextBox x:Name="txtDocNumber"
         AutomationProperties.LabeledBy="{Binding ElementName=lblDocNumber}"
         AutomationProperties.HelpText="Enter the document identification number"
         AutomationProperties.AutomationId="txtDocNumber" />
```

#### DataGrids
```xml
<!-- Before -->
<DataGrid ItemsSource="{Binding Documents}" />

<!-- After -->
<DataGrid ItemsSource="{Binding Documents}"
          AutomationProperties.Name="Documents List"
          AutomationProperties.HelpText="List of all imported documents. Use arrow keys to navigate."
          AutomationProperties.AutomationId="dgDocuments"
          AutomationProperties.ItemType="Document" />
```

#### ComboBoxes
```xml
<!-- Before -->
<ComboBox ItemsSource="{Binding Branches}" />

<!-- After -->
<ComboBox ItemsSource="{Binding Branches}"
          AutomationProperties.Name="Select Branch"
          AutomationProperties.HelpText="Choose the branch for document processing"
          AutomationProperties.AutomationId="cmbBranch" />
```

### Files Requiring Updates

Priority order for accessibility implementation:

1. **Views/WorkspaceView.xaml** - Main document workspace (30+ controls)
2. **Views/DashboardView.xaml** - Dashboard KPIs and charts (25+ controls)
3. **Views/SearchView.xaml** - Search and filter interface (20+ controls)
4. **Views/ImportView.xaml** - Document import (15+ controls)
5. **Views/ProcessingView.xaml** - Image processing tools (20+ controls)
6. **Views/ArchiveView.xaml** - Archive management (15+ controls)
7. **Views/ReportsView.xaml** - Report generation (15+ controls)
8. **Views/WebcamView.xaml** - Webcam capture (10+ controls)
9. **Dialogs/*.xaml** - All dialog windows (10+ files)

**Estimated Effort**: 15-20 hours for full implementation

### Testing Accessibility

#### Using Narrator (Windows Built-in)
```
1. Press Win+Ctrl+Enter to start Narrator
2. Use Tab to navigate between controls
3. Verify each control announces its name and purpose
4. Test keyboard navigation (Tab, Shift+Tab, Arrow keys)
5. Test Narrator reading order matches visual order
```

#### Using Accessibility Insights
Download from: https://accessibilityinsights.io/

Features:
- Automated accessibility testing
- Live inspection of AutomationProperties
- WCAG 2.1 compliance checking
- Keyboard navigation verification

### Keyboard Navigation Requirements

All interactive elements must be keyboard accessible:

| Element Type | Required Keys |
|--------------|---------------|
| Buttons | Enter, Space |
| TextBox | All keys, Tab to move |
| ComboBox | Up/Down arrows, Enter to select |
| DataGrid | Arrow keys, Tab, Enter |
| TreeView | Arrow keys, Space to expand |
| Menu | Alt, Arrow keys, Enter |

### Common Patterns

#### Modal Dialogs
```xml
<Window ...
        AutomationProperties.Name="Document Properties Dialog"
        AutomationProperties.HelpText="Edit document metadata and properties"
        FocusManager.FocusedElement="{Binding ElementName=txtFirstField}">
```

#### Status Messages
```xml
<TextBlock x:Name="txtStatus"
           AutomationProperties.LiveSetting="Polite"
           AutomationProperties.Name="Status Message" />
```

#### Progress Indicators
```xml
<ProgressBar Value="{Binding Progress}"
             AutomationProperties.Name="Import Progress"
             AutomationProperties.HelpText="{Binding ProgressText}"
             AutomationProperties.LiveSetting="Assertive" />
```

## Implementation Checklist

- [ ] Add AutomationProperties.Name to all Buttons
- [ ] Add AutomationProperties.LabeledBy to all TextBoxes
- [ ] Add AutomationProperties.HelpText to complex controls
- [ ] Add AutomationProperties.AutomationId for automation testing
- [ ] Test with Narrator
- [ ] Test keyboard-only navigation
- [ ] Verify tab order is logical
- [ ] Test with Accessibility Insights
- [ ] Document any accessibility limitations

## Compliance Standards

This implementation targets:
- **WCAG 2.1 Level AA** - Web Content Accessibility Guidelines
- **Section 508** - U.S. Federal accessibility requirements
- **EN 301 549** - European accessibility standard

## References

- [WPF Accessibility](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/advanced/accessibility-best-practices)
- [AutomationProperties Class](https://docs.microsoft.com/en-us/dotnet/api/system.windows.automation.automationproperties)
- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
