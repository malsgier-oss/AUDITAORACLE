using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace WorkAudit.Core.Helpers;

/// <summary>
/// Serializes daily journal content as RTF in <see cref="Domain.Note.Content"/>,
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
    public static bool IsDocumentEffectivelyEmpty(RichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        return string.IsNullOrWhiteSpace(range.Text);
    }

    /// <summary>
    /// Replaces the rich text box document with RTF or legacy plain text.
    /// </summary>
    public static void LoadInto(RichTextBox box, string? content)
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
    public static string SaveToRtfString(RichTextBox box)
    {
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Rtf);
        return Encoding.Default.GetString(ms.ToArray());
    }

    /// <summary>
    /// Loads journal content into a read-only viewer (same rules as <see cref="LoadInto"/>).
    /// </summary>
    public static void LoadIntoReadOnly(RichTextBox box, string? content, string emptyPlaceholder)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            box.Document.Blocks.Clear();
            box.Document.Blocks.Add(new Paragraph(new Run(emptyPlaceholder)));
            return;
        }

        LoadInto(box, content);
        if (IsDocumentEffectivelyEmpty(box) && !string.IsNullOrWhiteSpace(content) && !LooksLikeRtf(content))
        {
            box.Document.Blocks.Clear();
            box.Document.Blocks.Add(new Paragraph(new Run(content)));
        }
    }
}
