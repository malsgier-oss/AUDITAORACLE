namespace WorkAudit.Domain;

/// <summary>
/// Report attestation for digital sign-offs and approval workflow.
/// Tracks generate → review → approve with SHA256 hash for tamper detection.
/// </summary>
public class ReportAttestation
{
    public long Id { get; set; }
    public string Uuid { get; set; } = "";
    public string ReportType { get; set; } = "";
    public string ReportPath { get; set; } = "";
    public string DateFrom { get; set; } = "";
    public string DateTo { get; set; } = "";
    public string? Branch { get; set; }
    public string? Section { get; set; }
    public string Sha256Hash { get; set; } = "";
    public string Status { get; set; } = AttestationStatus.Generated;
    public string GeneratedAt { get; set; } = "";
    public string? GeneratedByUserId { get; set; }
    public string? GeneratedByUsername { get; set; }
    public string? ReviewedAt { get; set; }
    public string? ReviewedByUserId { get; set; }
    public string? ReviewedByUsername { get; set; }
    public string? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public string? ApprovedByUsername { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Attestation workflow status.</summary>
public static class AttestationStatus
{
    public const string Generated = "Generated";
    public const string Reviewed = "Reviewed";
    public const string Approved = "Approved";
}
