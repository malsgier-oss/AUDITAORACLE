using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using WpfRichTextBox = System.Windows.Controls.RichTextBox;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Serializes daily journal content as RTF in <see cref="WorkAudit.Domain.Note.Content"/>,
/// with backward-compatible loading of legacy plain-text entries.
/// </summary>
public static class JournalRtfSerializer
{
    public static bool LooksLikeRtf(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        var t = content.TrimStart();
        return t.StartsWith("{\\rtf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when the document has no meaningful text (whitespace only).
    /// </summary>
    public static bool IsDocumentEffectivelyEmpty(WpfRichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        return string.IsNullOrWhiteSpace(range.Text);
    }

    /// <summary>
    /// Replaces the rich text box document with RTF or legacy plain text.
    /// </summary>
    public static void LoadInto(WpfRichTextBox box, string? content)
    {
        box.Document.Blocks.Clear();

        if (string.IsNullOrWhiteSpace(content))
        {
            box.Document.Blocks.Add(new Paragraph());
            return;
        }

        if (LooksLikeRtf(content))
        {
            try
            {
                var bytes = Encoding.Default.GetBytes(content);
                using var ms = new MemoryStream(bytes);
                var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
                range.Load(ms, DataFormats.Rtf);
                return;
            }
            catch
            {
                // Fall through to plain text
            }
        }

        box.Document.Blocks.Clear();
        box.Document.Blocks.Add(new Paragraph(new Run(content)));
    }

    /// <summary>
    /// Serializes the entire document to an RTF string for storage.
    /// </summary>
    public static string SaveToRtfString(WpfRichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        return Encoding.Default.GetString(ms.ToArray());
    }
}
