using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WorkAudit.Views;

/// <summary>
/// Standalone window for previewing documents (images).
/// </summary>
public partial class DocumentPreviewWindow : Window
{
    private static DocumentPreviewWindow? _instance;

    public DocumentPreviewWindow()
    {
        InitializeComponent();
        Closed += (_, _) => _instance = null;
    }

    private void PreviewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    /// <summary>
    /// Shows or activates the Document Preview window. Call from anywhere in the app.
    /// </summary>
    public static void ShowOrActivate()
    {
        if (_instance == null)
            _instance = new DocumentPreviewWindow();
        _instance.Show();
        _instance.Activate();
    }

    /// <summary>
    /// Shows or activates the Document Preview window and displays the given document.
    /// </summary>
    public static void ShowOrActivateWithDocument(string filePath)
    {
        if (_instance == null)
            _instance = new DocumentPreviewWindow();
        _instance.Show();
        _instance.Activate();
        _instance.ShowDocument(filePath);
    }

    /// <summary>
    /// Loads and displays an image from the given file path.
    /// </summary>
    public void ShowDocument(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            DocumentPreviewImage.Source = null;
            DocumentNameText.Text = "";
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            DocumentPreviewImage.Source = bitmap;
            DocumentNameText.Text = Path.GetFileName(filePath);
            Title = $"Document Preview - {Path.GetFileName(filePath)}";
        }
        catch
        {
            DocumentPreviewImage.Source = null;
            DocumentNameText.Text = "Failed to load image";
        }
    }

    /// <summary>
    /// Clears the preview.
    /// </summary>
    public void Clear()
    {
        DocumentPreviewImage.Source = null;
        DocumentNameText.Text = "";
        Title = "Document Preview - WorkAudit";
    }
}
