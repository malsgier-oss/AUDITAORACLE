using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WorkAudit.Storage;

namespace WorkAudit.Dialogs;

public partial class ManageSavedConfigsDialog : Window
{
    private readonly ISavedReportConfigService _service;
    public SavedReportConfig? SelectedConfig { get; private set; }
    public bool LoadRequested { get; private set; }

    private sealed class ConfigItem { public SavedReportConfig Config { get; set; } = null!; public string DisplayName => (Config.IsFavorite ? "★ " : "") + Config.Name; }

    private readonly ObservableCollection<ConfigItem> _items = [];
    private ConfigItem? _draggedItem;
    private System.Windows.Point _dragStart;

    public ManageSavedConfigsDialog(ISavedReportConfigService service)
    {
        InitializeComponent();
        _service = service;
        ConfigListBox.ItemsSource = _items;
        RefreshList();
    }

    private void RefreshList()
    {
        _items.Clear();
        foreach (var c in _service.List())
            _items.Add(new ConfigItem { Config = c });
    }

    private void ConfigListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        FavoriteBtn.IsEnabled = ConfigListBox.SelectedItem is ConfigItem;
    }

    private void FavoriteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigListBox.SelectedItem is not ConfigItem item) return;
        item.Config.IsFavorite = !item.Config.IsFavorite;
        _service.Save(item.Config);
        RefreshList();
    }

    private void LoadBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigListBox.SelectedItem is ConfigItem item)
        {
            SelectedConfig = item.Config;
            LoadRequested = true;
            DialogResult = true;
            Close();
        }
        else
        {
            MessageBox.Show("Please select a configuration to load.", "Load", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (ConfigListBox.SelectedItem is not ConfigItem item)
        {
            MessageBox.Show("Please select a configuration to delete.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var cfg = item.Config;
        if (MessageBox.Show($"Delete \"{cfg.Name}\"?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _service.Delete(cfg.Id);
            RefreshList();
        }
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void ConfigListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item != null && item.DataContext is ConfigItem ci)
        {
            _draggedItem = ci;
            _dragStart = e.GetPosition(null);
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void ConfigListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedItem == null || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < 4 && Math.Abs(pos.Y - _dragStart.Y) < 4) return;
        DragDrop.DoDragDrop(ConfigListBox, _draggedItem, DragDropEffects.Move);
        _draggedItem = null;
    }

    private void ConfigListBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggedItem = null;
    }

    private void ConfigListBox_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ConfigItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ConfigListBox_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ConfigItem)) is not ConfigItem dropped || _items.Count == 0) return;
        var fromIdx = _items.IndexOf(dropped);
        if (fromIdx < 0) return;
        var targetItem = FindVisualParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        var toIdx = targetItem?.DataContext is ConfigItem target ? _items.IndexOf(target) : fromIdx;
        if (toIdx < 0) toIdx = fromIdx;
        if (fromIdx == toIdx) return;
        e.Handled = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _items.Move(fromIdx, toIdx);
            _service.Reorder(_items.Select(x => x.Config.Id).ToList());
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
