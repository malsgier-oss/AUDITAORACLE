using System;

namespace WorkAudit.Domain;

/// <summary>
/// Bank branch definitions. Single source of truth for branch names.
/// </summary>
public static class Branches
{
    /// <summary>Sentinel stored on <see cref="User.Branch"/> for users who may access every branch (non-managers).</summary>
    public const string AllBranchesLabel = "All Branches";

    public const string MainBranch = "Main Branch";
    public const string TripoliTowerBranch = "Tripoli Tower Branch";
    public const string SiahyaBranch = "Siahya Branch";
    public const string ZawiatDahmani = "Zawiat Dahmani";
    public const string AlmadarBranch = "Almadar Branch";
    public const string AlmashtelBranch = "Almashtel Branch";
    public const string MisrataBranch = "Misrata Branch";

    public static readonly string[] All =
    {
        MainBranch,
        TripoliTowerBranch,
        SiahyaBranch,
        ZawiatDahmani,
        AlmadarBranch,
        AlmashtelBranch,
        MisrataBranch
    };

    public static string Default => MainBranch;

    public static bool ScopesToAllBranches(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
            return false;
        return string.Equals(branch.Trim(), AllBranchesLabel, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves a user branch setting to a concrete branch name for capture paths, DB writes, and defaults.</summary>
    public static string ToConcreteBranchOrDefault(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || ScopesToAllBranches(branch))
            return Default;
        var t = branch.Trim();
        foreach (var b in All)
        {
            if (string.Equals(b, t, StringComparison.OrdinalIgnoreCase))
                return b;
        }
        return Default;
    }

    /// <param name="filterBranch">Concrete branch name, or null when UI means "all branches".</param>
    public static bool UserMatchesAssigneeBranchFilter(string? userBranch, string? filterBranch)
    {
        if (string.IsNullOrEmpty(filterBranch))
            return true;
        if (ScopesToAllBranches(userBranch))
            return true;
        return string.Equals(userBranch?.Trim(), filterBranch.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
