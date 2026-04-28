using System.Linq;

namespace WorkAudit.Domain;

/// <summary>
/// Document type and category mapping.
/// </summary>
public static class DocumentTypeInfo
{
    /// <summary>Picker value and default folder segment when no document type is set (replaces legacy generic "Other").</summary>
    public const string UnclassifiedType = "Unclassified";

    /// <summary>True when the type is unset, legacy <c>Other</c>, or explicitly <see cref="UnclassifiedType"/>.</summary>
    public static bool IsUnclassified(string? documentType)
    {
        if (string.IsNullOrWhiteSpace(documentType)) return true;
        var t = documentType.Trim();
        return string.Equals(t, "Other", StringComparison.OrdinalIgnoreCase)
               || string.Equals(t, UnclassifiedType, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Types shown in classification pickers: unclassified first, then configured types (no duplicate).</summary>
    public static List<string> BuildPickerItems(IEnumerable<string> configuredTypesOrdered)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { UnclassifiedType };
        var list = new List<string> { UnclassifiedType };
        foreach (var t in configuredTypesOrdered)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            if (seen.Add(t))
                list.Add(t);
        }
        return list;
    }

    /// <summary>Maps combo selection to DB: unclassified / Other / empty → empty string.</summary>
    public static string NormalizePickerToStorage(string? picked) =>
        IsUnclassified(picked) ? "" : (picked ?? "").Trim();

    /// <summary>Combo selected value for a document row (DB null/empty/Other → <see cref="UnclassifiedType"/>).</summary>
    public static string PickerSelectedValue(string? documentTypeFromDb) =>
        IsUnclassified(documentTypeFromDb) ? UnclassifiedType : documentTypeFromDb!.Trim();

    /// <summary>Folder / rename segment when <paramref name="storedType"/> is empty (imports, paths).</summary>
    public static string FolderSegmentForType(string? storedType) =>
        string.IsNullOrWhiteSpace(storedType) ? UnclassifiedType : storedType.Trim();

    public static readonly Dictionary<string, string> DocTypeToCategory = new()
    {
        ["Cash Deposit Slip"] = "CASH",
        ["Cheque Book Request Form"] = "CHEQUE",
        ["Account Statement Request"] = "ACCOUNT",
        ["Internal Transfer Form"] = "ACCOUNT",
        ["Card Receipt & Authorization"] = "CARD",
        ["3D Secure Subscription / Cancellation"] = "CARD",
        ["Cards Dispute Form"] = "CARD",
        ["Foreign Exchange & Transfer Request (MoneyGram)"] = "TRANSFER",
        ["Change Beneficiary / Country (MoneyGram)"] = "TRANSFER",
        ["MoneyGram"] = "TRANSFER",
        ["Know Your Customer (KYC) Form"] = "KYC",
        ["Withdrawal Slip"] = "CASH",
        ["Power of Attorney"] = "OTHER",
        ["Account Opening Form"] = "ACCOUNT",
        ["Signature Card"] = "OTHER",
        ["Stop Payment Request"] = "CHEQUE"
    };

    public static readonly string[] Categories = { "CASH", "CHEQUE", "ACCOUNT", "CARD", "TRANSFER", "KYC", "OTHER" };

    public static readonly string[] AllDocTypes = DocTypeToCategory.Keys.OrderBy(k => k).ToArray();
}
