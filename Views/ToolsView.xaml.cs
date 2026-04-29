using System.Windows.Controls;
using WorkAudit.Views.Tools;

namespace WorkAudit.Views;

public partial class ToolsView : UserControl
{
    public ToolsView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (BackupHost != null)
            BackupHost.Content = new BackupToolsPanel();
    }
}
