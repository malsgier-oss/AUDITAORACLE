using System.Globalization;
using System.Text.RegularExpressions;

namespace WorkAudit.Core.TextExtraction;

public sealed class OcrStructuredDataResult
{
    public string? AccountName { get; init; }
    public int AccountNameConfidence { get; init; }
    public string? AccountNumber { get; init; }
    public int AccountNumberConfidence { get; init; }
    public string? TransactionReference { get; init; }
    public int TransactionReferenceConfidence { get; init; }
    public string? ExtractedDate { get; init; }
    public int ExtractedDateConfidence { get; init; }
    public string? Amounts { get; init; }
    public int AmountsConfidence { get; init; }
}

public static partial class OcrStructuredDataExtractor
{
    [GeneratedRegex(@"(?im)(?:account(?:\s*name)?|customer|beneficiary|name|اسم(?:\s*الحساب)?)\s*[:\-]\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex NameRegex();

    [GeneratedRegex(@"(?im)(?:account(?:\s*(?:no|number|#))?|iban|رقم(?:\s*الحساب)?)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-\s]{5,40})$", RegexOptions.CultureInvariant)]
    private static partial Regex AccountNumberRegex();

    [GeneratedRegex(@"(?im)(?:ref(?:erence)?|txn|transaction(?:\s*ref(?:erence)?)?|cheque|authorization|مرجع|رقم(?:\s*المرجع)?)\s*[:\-]?\s*([A-Z0-9][A-Z0-9\-/\s]{3,40})$", RegexOptions.CultureInvariant)]
    private static partial Regex ReferenceRegex();

    [GeneratedRegex(@"\b((?:19|20)\d{2}[\/\-.](?:0?[1-9]|1[0-2])[\/\-.](?:0?[1-9]|[12]\d|3[01])|(?:0?[1-9]|[12]\d|3[01])[\/\-.](?:0?[1-9]|1[0-2])[\/\-.](?:19|20)\d{2})\b", RegexOptions.CultureInvariant)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"(?im)(?:amount|total|sum|net|gross|المبلغ|الإجمالي)\s*[:\-]?\s*([0-9][0-9,\.\s]{1,20})", RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    public static OcrStructuredDataResult Extract(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new OcrStructuredDataResult();

        var accountName = ExtractLineValue(NameRegex(), text, minLen: 3, maxLen: 120);
        var accountNumber = ExtractLineValue(AccountNumberRegex(), text, minLen: 6, maxLen: 40, stripSpaces: true);
        var reference = ExtractLineValue(ReferenceRegex(), text, minLen: 4, maxLen: 40, stripSpaces: true);
        var date = ExtractDate(text);
        var amount = ExtractAmount(text);

        return new OcrStructuredDataResult
        {
            AccountName = accountName.value,
            AccountNameConfidence = accountName.confidence,
            AccountNumber = accountNumber.value,
            AccountNumberConfidence = accountNumber.confidence,
            TransactionReference = reference.value,
            TransactionReferenceConfidence = reference.confidence,
            ExtractedDate = date.value,
            ExtractedDateConfidence = date.confidence,
            Amounts = amount.value,
            AmountsConfidence = amount.confidence
        };
    }

    private static (string? value, int confidence) ExtractLineValue(Regex regex, string text, int minLen, int maxLen, bool stripSpaces = false)
    {
        var match = regex.Match(text);
        if (!match.Success)
            return (null, 0);

        var raw = match.Groups[1].Value.Trim();
        if (stripSpaces)
            raw = raw.Replace(" ", string.Empty);

        raw = raw.Trim('-', ':', ';', '.', ',', '/', '\\');
        if (raw.Length < minLen || raw.Length > maxLen)
            return (null, 0);

        var confidence = 70;
        if (raw.Any(char.IsDigit) && raw.Any(char.IsLetter))
            confidence += 5;
        if (raw.Any(c => c == '?' || c == '\uFFFD'))
            confidence -= 25;
        confidence = Math.Clamp(confidence, 0, 95);

        return (raw, confidence);
    }

    private static (string? value, int confidence) ExtractDate(string text)
    {
        var match = DateRegex().Match(text);
        if (!match.Success)
            return (null, 0);

        var raw = match.Groups[1].Value.Replace('.', '/').Replace('-', '/');
        var formats = new[]
        {
            "yyyy/M/d", "yyyy/MM/dd",
            "d/M/yyyy", "dd/MM/yyyy",
            "M/d/yyyy", "MM/dd/yyyy"
        };

        if (!DateTime.TryParseExact(raw, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return (null, 0);
        }

        return (dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 80);
    }

    private static (string? value, int confidence) ExtractAmount(string text)
    {
        var match = AmountRegex().Match(text);
        if (!match.Success)
            return (null, 0);

        var raw = match.Groups[1].Value.Trim();
        var cleaned = raw.Replace(" ", string.Empty);

        if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var inv))
            return (inv.ToString("0.##", CultureInfo.InvariantCulture), 78);

        var normalized = cleaned.Replace(".", "").Replace(",", ".");
        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var euro))
            return (euro.ToString("0.##", CultureInfo.InvariantCulture), 74);

        return (null, 0);
    }
}
