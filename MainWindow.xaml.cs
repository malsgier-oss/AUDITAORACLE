using System.IO;
using System.Windows;
using System.Windows.Shell;
using Serilog;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WorkAudit.Config;
using WorkAudit.Core;
using WorkAudit.Core.Backup;
using WorkAudit.Core.Compliance;
using WorkAudit.Core.Export;
using WorkAudit.Core.Reports;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Views;
using WorkAudit.Views.Admin;

namespace WorkAudit;

public partial class MainWindow : Window
{
    private readonly IDocumentStore _store = null!;
    private readonly string _baseDir = null!;
    private readonly UserControl[] _views = null!;
    private int _currentView;
    private bool _sidebarVisible = true;
    private bool _borderlessFullscreen;
    private WindowState _windowStateBeforeBorderless = WindowState.Normal;
    private System.Windows.Rect _restoreBoundsBeforeFullscreen;
    private ResizeMode _resizeModeBeforeFullscreen = ResizeMode.CanResize;
    private DispatcherTimer? _inactivityTimer;
    private DateTime _lastActivityRecorded = DateTime.MinValue;
    public static bool LogoutRequested { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        var config = ServiceContainer.GetService<AppConfiguration>();
        if (config == null)
        {
            Log.Error("AppConfiguration not available from ServiceContainer");
            MessageBox.Show("Application configuration error. Please restart the application.",
                "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current.Shutdown();
            return;
        }
        _baseDir = config.BaseDirectory;
        Directory.CreateDirectory(_baseDir);
        _store = ServiceContainer.GetService<IDocumentStore>();
        if (UserDisplay != null)
            UserDisplay.Text = config?.CurrentUserName != null ? $"Signed in as {config.CurrentUserName}" : "";

        var searchExportService = ServiceContainer.GetService<ISearchExportService>();
        var docTypeService = ServiceContainer.GetService<IDocumentTypeService>();
        var auditTrailService = ServiceContainer.GetService<IAuditTrailService>();
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        var archiveService = ServiceContainer.GetService<IArchiveService>();
        var legalHoldService = ServiceContainer.GetService<ILegalHoldService>();
        var immutabilityService = ServiceContainer.GetService<IImmutabilityService>();
        var chainOfCustodyService = ServiceContainer.GetService<IChainOfCustodyService>();
        var savedArchiveSearchService = ServiceContainer.GetService<ISavedArchiveSearchService>();
        var exportEncryptionService = ServiceContainer.GetService<IExportEncryptionService>();
        var custodianService = ServiceContainer.GetService<ICustodianService>();
        var disposalService = ServiceContainer.GetService<IDisposalService>();
        var sessionService = ServiceContainer.GetService<ISessionService>();
        
        // Determine which dashboard to show based on role
        var roleLevel = Roles.GetRoleLevel(config?.CurrentUserRole ?? Roles.Viewer);
        UserControl dashboardView;
        
        if (roleLevel >= 4) // Manager or Administrator
        {
            dashboardView = new DashboardView(); // Manager/Admin Dashboard
        }
        else if (roleLevel >= 2) // Auditor or Reviewer
        {
            dashboardView = new AuditorDashboardView(); // Auditor Dashboard
        }
        else // Viewer or no role
        {
            dashboardView = new InputView(); // No dashboard for Viewers
        }
        
        _views = new UserControl[]
        {
            dashboardView,                                                                   // 0: Dashboard (role-specific)
            new InputView(),                                                                 // 1: Input (Import + Webcam)
            new ProcessingView(),                                                            // 2: Processing
            new WorkspaceView(_store, _baseDir, docTypeService),                             // 3: Workspace
            new ArchiveView(_store, docTypeService, searchExportService, archiveService, legalHoldService, immutabilityService, chainOfCustodyService, auditTrailService, permissionService, savedArchiveSearchService, exportEncryptionService, custodianService, disposalService, sessionService), // 4: Archive
            new ToolsView(),                                                                 // 5: Tools
            new ReportsView(),                                                               // 6: Reports (Manager+)
            new AuditorReportsView(),                                                        // 7: Auditor Reports (Auditor/Reviewer)
            new ReportEditorView()                                                           // 8: Report Editor (draft HTML)
        };

        // Start at Dashboard for users with role >= Auditor, otherwise start at Input
        var canAccessDashboard = roleLevel >= 2;
        ContentHost.Content = canAccessDashboard ? _views[0] : _views[1];
        _currentView = canAccessDashboard ? 0 : 1;
        UpdateActivityButtons();
        UpdateInputMetadataToggleButtonVisibility();
        SyncInputMetadataToggleGlyph();
        DrillDownRequested.Requested += OnDrillDownRequested;
        DocumentsArchivedRequested.Requested += OnDocumentsArchived;
        DocumentsMovedToWorkspaceRequested.Requested += OnDocumentsMovedToWorkspace;
        UpdateRbacVisibility();
        StartInactivityMonitoring();
        ServiceContainer.GetService<IScheduledBackupService>().Start();
        ServiceContainer.GetService<IScheduledReportService>().Start();

        var progressService = ServiceContainer.GetService<IProcessingProgressService>();
        progressService.ProgressChanged += OnProcessingProgressChanged;

        Loaded += (_, _) => ApplyLocalization();
        SourceInitialized += MainWindow_SourceInitialized;
        StateChanged += (_, _) => UpdateResizeBorderForWindowState();
        UpdateResizeBorderForWindowState();
    }

    private void UpdateResizeBorderForWindowState()
    {
        if (WindowChrome.GetWindowChrome(this) is not { } chrome) return;
        if (_borderlessFullscreen)
        {
            chrome.ResizeBorderThickness = new Thickness(0);
            return;
        }
        chrome.ResizeBorderThickness = WindowState == WindowState.Maximized
            ? new Thickness(0)
            : new Thickness(6);
    }

    private void ApplyLocalization()
    {
        var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
        if (config == null) return;

        var isArabic = ReportLocalizationService.IsArabic(config);
        FlowDirection = isArabic ? System.Windows.FlowDirection.RightToLeft : System.Windows.FlowDirection.LeftToRight;

        Title = ReportLocalizationService.GetString("AppTitle", config);
        if (StatusText != null) StatusText.Text = ReportLocalizationService.GetString("Ready", config);

        if (MenuFile != null) MenuFile.Header = ReportLocalizationService.GetString("File", config);
        if (MenuOpenFiles != null) MenuOpenFiles.Header = ReportLocalizationService.GetString("OpenFiles", config);
        if (MenuOpenFolder != null) MenuOpenFolder.Header = ReportLocalizationService.GetString("OpenFolder", config);
        if (FileLogout != null) FileLogout.Header = ReportLocalizationService.GetString("SignOut", config);
        if (MenuExit != null) MenuExit.Header = ReportLocalizationService.GetString("Exit", config);

        if (MenuEdit != null) MenuEdit.Header = ReportLocalizationService.GetString("Edit", config);
        if (MenuPreferences != null) MenuPreferences.Header = ReportLocalizationService.GetString("Preferences", config);

        if (MenuView != null) MenuView.Header = ReportLocalizationService.GetString("View", config);
        if (MenuProperties != null) MenuProperties.Header = ReportLocalizationService.GetString("Properties", config);
        if (MenuGoTo != null) MenuGoTo.Header = ReportLocalizationService.GetString("GoTo", config);
        if (MenuGoDashboard != null) MenuGoDashboard.Header = ReportLocalizationService.GetString("Dashboard", config);
        if (MenuGoInput != null) MenuGoInput.Header = ReportLocalizationService.GetString("Input", config);
        if (MenuGoProcessing != null) MenuGoProcessing.Header = ReportLocalizationService.GetString("Processing", config);
        if (MenuGoWorkspace != null) MenuGoWorkspace.Header = ReportLocalizationService.GetString("Workspace", config);
        if (MenuGoArchive != null) MenuGoArchive.Header = ReportLocalizationService.GetString("Archive", config);
        if (MenuGoDocumentPreview != null) MenuGoDocumentPreview.Header = ReportLocalizationService.GetString("DocumentPreview", config);
        if (MenuGoReports != null) MenuGoReports.Header = ReportLocalizationService.GetString("Reports", config);
        if (MenuGoAssignments != null) MenuGoAssignments.Header = ReportLocalizationService.GetString("MyAssignments", config);
        if (MenuToggleSidebar != null) MenuToggleSidebar.Header = ReportLocalizationService.GetString("ToggleSidebar", config);

        if (MenuControlPanel != null) MenuControlPanel.Header = ReportLocalizationService.GetString("ControlPanel", config);

        if (AdminMenu != null) AdminMenu.Header = ReportLocalizationService.GetString("Admin", config);
        if (MenuUserManagement != null) MenuUserManagement.Header = ReportLocalizationService.GetString("UserManagement", config);
        if (MenuAssignmentManagement != null) MenuAssignmentManagement.Header = ReportLocalizationService.GetString("AssignmentManagement", config);
        if (MenuTeamTaskManagement != null) MenuTeamTaskManagement.Header = ReportLocalizationService.GetString("TeamTaskManagement", config);
        if (MenuAuditLog != null) MenuAuditLog.Header = ReportLocalizationService.GetString("AuditLog", config);

        if (MenuHelp != null) MenuHelp.Header = ReportLocalizationService.GetString("Help", config);
        if (MenuAbout != null) MenuAbout.Header = ReportLocalizationService.GetString("About", config);

        if (ActDashboard != null) { ActDashboard.Content = ReportLocalizationService.GetString("Dashboard", config); ActDashboard.ToolTip = ReportLocalizationService.GetString("TooltipDashboard", config); }
        if (ActInput != null) { ActInput.Content = ReportLocalizationService.GetString("Input", config); ActInput.ToolTip = ReportLocalizationService.GetString("TooltipImport", config); }
        if (ActProcessing != null) { ActProcessing.Content = ReportLocalizationService.GetString("Processing", config); ActProcessing.ToolTip = ReportLocalizationService.GetString("TooltipProcessing", config); }
        if (ActWorkspace != null) { ActWorkspace.Content = ReportLocalizationService.GetString("Workspace", config); ActWorkspace.ToolTip = ReportLocalizationService.GetString("TooltipWorkspace", config); }
        if (ActArchive != null) { ActArchive.Content = ReportLocalizationService.GetString("Archive", config); ActArchive.ToolTip = ReportLocalizationService.GetString("TooltipArchive", config); }
        if (ActReports != null) { ActReports.Content = ReportLocalizationService.GetString("Reports", config); ActReports.ToolTip = ReportLocalizationService.GetString("TooltipReports", config); }


        if (ProgressCancelBtn != null) ProgressCancelBtn.Content = ReportLocalizationService.GetString("Cancel", config);

        var appConfig = ServiceContainer.GetService<AppConfiguration>();
        if (UserDisplay != null && appConfig?.CurrentUserName != null)
            UserDisplay.Text = ReportLocalizationService.GetString("SignedInAs", config, appConfig.CurrentUserName);
    }

    private void OnProcessingProgressChanged(object? sender, ProcessingProgressEventArgs e)
    {
        if (ProgressBarContainer == null || ProcessingProgressBar == null) return;
        var visible = e.IsVisible;
        ProgressBarContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (StatusText != null)
        {
            var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            StatusText.Text = visible ? e.Message : (config != null ? ReportLocalizationService.GetString("Ready", config) : "Ready");
        }
        if (visible)
        {
            ProcessingProgressBar.IsIndeterminate = e.IsIndeterminate;
            if (!e.IsIndeterminate)
                ProcessingProgressBar.Value = e.Percent;
        }
    }

    private void ProgressCancelBtn_Click(object sender, RoutedEventArgs e)
    {
        ServiceContainer.GetService<IProcessingProgressService>().Cancel();
    }

    private void StartInactivityMonitoring()
    {
        PreviewMouseMove += OnUserActivity;
        PreviewKeyDown += OnPreviewKeyDownDelete;
        PreviewKeyDown += OnUserActivity;

        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(1)
        };
        _inactivityTimer.Tick += async (_, _) =>
        {
            var sessionService = ServiceContainer.GetService<ISessionService>();
            if (await sessionService.CheckInactivityTimeoutAsync())
            {
                _inactivityTimer?.Stop();
                MessageBox.Show(
                    "You have been signed out due to inactivity.",
                    "Session Expired",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                LogoutRequested = true;
                Close();
            }
        };
        _inactivityTimer.Start();
    }

    private void OnPreviewKeyDownDelete(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        if (ContentHost?.Content is Core.IDeleteKeyHandler handler && handler.TryHandleDeleteKey())
            e.Handled = true;
    }

    private void OnUserActivity(object sender, EventArgs e)
    {
        if (DateTime.UtcNow - _lastActivityRecorded < TimeSpan.FromSeconds(30))
            return;

        _lastActivityRecorded = DateTime.UtcNow;
        ServiceContainer.GetService<ISessionService>().RecordActivity();
    }

    protected override void OnClosed(EventArgs e)
    {
        // Unsubscribe from event handlers to prevent memory leaks
        PreviewMouseMove -= OnUserActivity;
        PreviewKeyDown -= OnPreviewKeyDownDelete;
        PreviewKeyDown -= OnUserActivity;

        _inactivityTimer?.Stop();
        _inactivityTimer = null;
        base.OnClosed(e);
    }

    private void UpdateRbacVisibility()
    {
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        AdminMenu.Visibility = permissionService.HasPermission(Permissions.UserView)
            ? Visibility.Visible
            : Visibility.Collapsed;
        
        // Dashboard - Auditor+ (role level >= 2)
        var canAccessDashboard = permissionService.HasMinimumRole(Roles.Auditor);
        if (ActDashboard != null) ActDashboard.Visibility = canAccessDashboard ? Visibility.Visible : Visibility.Collapsed;
        if (MenuGoDashboard != null) MenuGoDashboard.Visibility = canAccessDashboard ? Visibility.Visible : Visibility.Collapsed;
        if (ActivityBarDashboardSeparator != null) ActivityBarDashboardSeparator.Visibility = canAccessDashboard ? Visibility.Visible : Visibility.Collapsed;
        
        // Reports - Auditor+ (Auditors see "My Reports", Managers see full Reports)
        var canAccessReports = permissionService.HasMinimumRole(Roles.Auditor);
        if (ActReports != null) ActReports.Visibility = canAccessReports ? Visibility.Visible : Visibility.Collapsed;
        if (MenuGoReports != null) MenuGoReports.Visibility = canAccessReports ? Visibility.Visible : Visibility.Collapsed;
        if (ActivityBarReportsSeparator != null) ActivityBarReportsSeparator.Visibility = canAccessReports ? Visibility.Visible : Visibility.Collapsed;
        if (MenuGoDashboardSeparator != null) MenuGoDashboardSeparator.Visibility = canAccessDashboard ? Visibility.Visible : Visibility.Collapsed;

        var isManager = permissionService.HasMinimumRole(Roles.Manager);
        if (MenuControlPanel != null) MenuControlPanel.Visibility = isManager ? Visibility.Visible : Visibility.Collapsed;
        if (MenuProperties != null) MenuProperties.Visibility = isManager ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateActivityButtons()
    {
        var buttons = new[] { ActDashboard, ActInput, ActProcessing, ActWorkspace, ActArchive, null, ActReports, null };
        for (var i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            if (btn != null)
            {
                var isActive = i == _currentView || (i == 6 && (_currentView == 7 || _currentView == 8));
                btn.Opacity = isActive ? 1.0 : 0.7;
            }
        }
    }

    private void SwitchToView(int index)
    {
        if (index < 0 || index >= _views.Length) return;
        _currentView = index;
        ContentHost.Content = _views[index];
        if (index == 4 && _views[4] is ArchiveView archiveView)
            archiveView.Refresh();
        UpdateActivityButtons();
        UpdateInputMetadataToggleButtonVisibility();
        SyncInputMetadataToggleGlyph();
    }

    private void UpdateInputMetadataToggleButtonVisibility()
    {
        if (BtnToggleInputMetadata != null)
            BtnToggleInputMetadata.Visibility = _currentView == 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncInputMetadataToggleGlyph()
    {
        if (ToggleInputMetadataIcon == null || BtnToggleInputMetadata == null) return;
        var expanded = _views.Length > 1 && _views[1] is InputView iv && iv.IsSharedMetadataVisible;
        ToggleInputMetadataIcon.Text =
            expanded
                ? (TryFindResource("IconUp") as string) ?? "\uE74A"
                : (TryFindResource("IconDown") as string) ?? "\uE74B";
        BtnToggleInputMetadata.ToolTip = expanded
            ? "Hide Branch, Date, and classification fields for more preview space."
            : "Show Branch, Date, and classification fields.";
    }

    private void BtnToggleInputMetadata_Click(object sender, RoutedEventArgs e)
    {
        if (_views.Length <= 1 || _views[1] is not InputView inputView) return;
        inputView.SetSharedMetadataVisible(!inputView.IsSharedMetadataVisible);
        SyncInputMetadataToggleGlyph();
    }

    private void ActDashboard_Click(object sender, RoutedEventArgs e) => SwitchToView(0);
    private void ActInput_Click(object sender, RoutedEventArgs e) => SwitchToView(1);
    private void ActProcessing_Click(object sender, RoutedEventArgs e) => SwitchToView(2);
    private void ActWorkspace_Click(object sender, RoutedEventArgs e) => SwitchToView(3);

    private void OnDrillDownRequested(DrillDownRequest request)
    {
        SwitchToView(3);
        if (_views[3] is WorkspaceView ws)
        {
            ws.ApplyDrillDownFilter(request.Branch, request.Section, request.DateFrom, request.DateTo);
            if (request.DocumentId is { } docId)
            {
                // Defer until Workspace list is bound/updated after switching content.
                Dispatcher.BeginInvoke(new Action(() => ws.SelectDocumentById(docId)), DispatcherPriority.Loaded);
            }
        }
    }

    private void OnDocumentsArchived(int count)
    {
        if (StatusText != null)
        {
            var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            var msg = config != null
                ? ReportLocalizationService.GetString("DocumentsArchived", config, count)
                : $"{count} document(s) archived.";
            StatusText.Text = msg;
        }
    }

    private void OnDocumentsMovedToWorkspace(int count)
    {
        if (StatusText != null)
        {
            var config = ServiceContainer.IsInitialized ? ServiceContainer.GetService<IConfigStore>() : null;
            StatusText.Text = config != null
                ? ReportLocalizationService.GetString("DocumentsMovedToWorkspace", config, count)
                : $"{count} document(s) moved to Workspace.";
        }
        if (_views[3] is WorkspaceView ws)
            ws.RefreshDocumentList();
    }
    private void ActArchive_Click(object sender, RoutedEventArgs e) => SwitchToView(4);
    private void ActTools_Click(object sender, RoutedEventArgs e) => SwitchToView(5);
    private void ActReports_Click(object sender, RoutedEventArgs e)
    {
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        if (permissionService.HasMinimumRole(Roles.Manager))
            SwitchToView(6);
        else
            SwitchToView(7);
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (!IsInTitleBar(e.OriginalSource)) return;
        // Don't start drag from caption buttons ΓÇö let them handle click (source may be child of Button)
        if (IsInButtonBase(e.OriginalSource)) return;
        // Don't handle when click is on or inside a menu item ΓÇö let the menu open
        if (IsInMenuItem(e.OriginalSource)) return;

        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
        e.Handled = true;
    }

    private void Window_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        // Snap to top edge when dragging (like Windows Aero Snap)
        if (e.LeftButton == MouseButtonState.Pressed && WindowState != WindowState.Maximized)
        {
            var screenPoint = PointToScreen(e.GetPosition(this));
            if (screenPoint.Y <= 5)
                WindowState = WindowState.Maximized;
        }
    }

    private bool IsInTitleBar(object? element)
    {
        if (element is not System.Windows.DependencyObject d) return false;
        var current = d;
        while (current != null)
        {
            if (current == TitleBar) return true;
            current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : System.Windows.LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    /// <summary>True if the element is a ButtonBase or inside one (so caption buttons receive the click).</summary>
    private static bool IsInButtonBase(object? element)
    {
        if (element is not System.Windows.DependencyObject d) return false;
        var current = d;
        while (current != null)
        {
            if (current is System.Windows.Controls.Primitives.ButtonBase) return true;
            current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : System.Windows.LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    /// <summary>True if the element is a MenuItem or inside one (so we don't steal the click and block the menu).</summary>
    private static bool IsInMenuItem(object? element)
    {
        if (element is not System.Windows.DependencyObject d) return false;
        var current = d;
        while (current != null)
        {
            if (current is System.Windows.Controls.MenuItem) return true;
            current = current is System.Windows.Media.Visual || current is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                : System.Windows.LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F11) return;
        e.Handled = true;
        ToggleBorderlessFullscreen();
    }

    /// <summary>F11: true fullscreen on the current monitor (edge to edge, including over the taskbar).</summary>
    private void ToggleBorderlessFullscreen()
    {
        if (RootChromeBorder == null) return;

        if (!_borderlessFullscreen)
        {
            _resizeModeBeforeFullscreen = ResizeMode;
            _restoreBoundsBeforeFullscreen = RestoreBounds;
            _windowStateBeforeBorderless = WindowState;

            RootChromeBorder.BorderThickness = new Thickness(0);
            _borderlessFullscreen = true;

            WindowState = WindowState.Normal;
            ApplyFullMonitorBounds();
            ResizeMode = ResizeMode.NoResize;
            UpdateResizeBorderForWindowState();
        }
        else
        {
            ResizeMode = _resizeModeBeforeFullscreen;
            RootChromeBorder.BorderThickness = new Thickness(1);
            _borderlessFullscreen = false;

            WindowState = WindowState.Normal;
            Left = _restoreBoundsBeforeFullscreen.Left;
            Top = _restoreBoundsBeforeFullscreen.Top;
            Width = _restoreBoundsBeforeFullscreen.Width;
            Height = _restoreBoundsBeforeFullscreen.Height;
            WindowState = _windowStateBeforeBorderless;
            UpdateResizeBorderForWindowState();
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    private void BtnClose_MouseEnter(object sender, MouseEventArgs e) => BtnClose.Background = System.Windows.Media.Brushes.DarkRed;
    private void BtnClose_MouseLeave(object sender, MouseEventArgs e) => BtnClose.Background = System.Windows.Media.Brushes.Transparent;

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarVisible = !_sidebarVisible;
        ApplySidebarVisibility();
    }


    private void ApplySidebarVisibility()
    {
        ActivityBar.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        ActivityBarRow.Height = _sidebarVisible ? new GridLength(40) : new GridLength(0);
        ActivityBarRow.MinHeight = _sidebarVisible ? 40 : 0;
        ActivityBarRow.MaxHeight = _sidebarVisible ? 40 : 0;
        if (ActivityBarSplitterRow != null)
            ActivityBarSplitterRow.Height = _sidebarVisible ? new GridLength(1) : new GridLength(0);
        if (ActivityBarSeparator != null)
            ActivityBarSeparator.Visibility = _sidebarVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Document",
            Filter = "All supported|*.pdf;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tiff;*.tif|PDF files|*.pdf|Image files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tiff;*.tif|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
        {
            // Switch to Import view and trigger import
            SwitchToView(1);
            if (_views[1] is InputView inputView)
            {
                _ = inputView.ImportFilesAsync(dlg.FileNames).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                        Log.Warning(t.Exception, "Import failed");
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }

    private void FileOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder containing documents to import",
            ShowNewFolderButton = false
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dlg.SelectedPath))
        {
            // Switch to Import view and trigger folder import
            SwitchToView(1);
            if (_views[1] is InputView inputView)
            {
                _ = inputView.ImportFolderAsync(dlg.SelectedPath).ContinueWith(t =>
                {
                    if (t.IsFaulted && t.Exception != null)
                        Log.Warning(t.Exception, "Folder import failed");
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
        }
    }
    private async void FileLogout_Click(object sender, RoutedEventArgs e)
    {
        var sessionService = ServiceContainer.GetService<Core.Security.ISessionService>();
        await sessionService.LogoutAsync();
        LogoutRequested = true;
        Close();
    }
    private void FileExit_Click(object sender, RoutedEventArgs e)
    {
        LogoutRequested = false;
        Close();
    }
    private void EditPreferences_Click(object sender, RoutedEventArgs e)
    {
        var win = new ControlPanelWindow { Owner = this };
        win.ShowDialog();
        ApplyLocalization();
    }

    private void ToolsControlPanel_Click(object sender, RoutedEventArgs e)
    {
        var win = new ControlPanelWindow { Owner = this };
        win.ShowDialog();
        ApplyLocalization();
    }

    private void ViewProperties_Click(object sender, RoutedEventArgs e)
    {
        // Show Control Panel for application properties/settings
        var win = new ControlPanelWindow { Owner = this };
        win.ShowDialog();
        ApplyLocalization();
    }
    private void GoDashboard_Click(object sender, RoutedEventArgs e) => SwitchToView(0);
    private void GoInput_Click(object sender, RoutedEventArgs e) => SwitchToView(1);
    private void GoProcessing_Click(object sender, RoutedEventArgs e) => SwitchToView(2);
    private void GoWorkspace_Click(object sender, RoutedEventArgs e) => SwitchToView(3);
    private void GoArchive_Click(object sender, RoutedEventArgs e) => SwitchToView(4);
    private void GoDocumentPreview_Click(object sender, RoutedEventArgs e) => SwitchToView(3); // Workspace contains Document Preview
    private void GoTools_Click(object sender, RoutedEventArgs e) => SwitchToView(5);
    private void GoReports_Click(object sender, RoutedEventArgs e)
    {
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        if (permissionService.HasMinimumRole(Roles.Manager))
            SwitchToView(6);
        else
            SwitchToView(7);
    }
    private void GoAssignments_Click(object sender, RoutedEventArgs e)
    {
        SwitchToView(3);
        if (_views[3] is WorkspaceView ws)
            ws.SelectMyAssignments();
    }
    private void AdminUserManagement_Click(object sender, RoutedEventArgs e)
    {
        var win = new UserManagementWindow { Owner = this };
        win.ShowDialog();
    }

    private void AdminAssignmentManagement_Click(object sender, RoutedEventArgs e)
    {
        var win = new AssignmentManagementWindow { Owner = this };
        win.ShowDialog();
    }

    private void AdminTeamTaskManagement_Click(object sender, RoutedEventArgs e)
    {
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        if (!permissionService.HasPermission(Permissions.TeamTasksManage))
        {
            MessageBox.Show("You do not have permission to manage team tasks.", "Team tasks",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        var win = new TeamTaskManagementWindow { Owner = this };
        win.ShowDialog();
    }

    private void AdminAuditLog_Click(object sender, RoutedEventArgs e)
    {
        var win = new AuditLogWindow { Owner = this };
        win.ShowDialog();
    }

    private void HelpAbout_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Dialogs.AboutDialog { Owner = this };
        dlg.ShowDialog();
    }

    // Public navigation methods for DashboardView
    public void NavigateToWorkspace() => SwitchToView(3);
    public void NavigateToReports() => SwitchToView(6);
    public void NavigateToArchive() => SwitchToView(4);
    public void NavigateToView(int viewIndex) => SwitchToView(viewIndex);

    /// <summary>Opens the report editor and loads the given draft (Managers use full Reports view; others use Auditor Reports).</summary>
    public void NavigateToReportEditor(int draftId)
    {
        SwitchToView(8);
        if (_views[8] is ReportEditorView editor)
            editor.LoadDraft(draftId);
    }

    /// <summary>Returns to the Reports area matching the current user's role (same as the Reports activity button).</summary>
    public void NavigateToReportsForCurrentRole()
    {
        var permissionService = ServiceContainer.GetService<IPermissionService>();
        if (permissionService.HasMinimumRole(Roles.Manager))
            SwitchToView(6);
        else
            SwitchToView(7);
    }
}
