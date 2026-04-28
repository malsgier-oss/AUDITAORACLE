using System.IO;
using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Security;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Reports;

/// <summary>
/// Report attestation and approval workflow.
/// SHA256 hash for tamper detection; generate → review → approve.
/// </summary>
public interface IReportAttestationService
{
    ReportAttestation CreateAttestation(string reportType, string reportPath, DateTime from, DateTime to, string? branch, string? section, string? userId, string? username);
    ReportAttestation? GetByReportPath(string reportPath);
    bool VerifyHash(string reportPath, string expectedHash);
    void MarkReviewed(long attestationId, string userId, string username);
    void MarkApproved(long attestationId, string userId, string username);
    /// <summary>Recompute SHA-256 and persist after the PDF on disk was modified (e.g. attestation page appended).</summary>
    void RefreshFileHash(string reportPath);
}

public class ReportAttestationService : IReportAttestationService
{
    private readonly ILogger _log = LoggingService.ForContext<ReportAttestationService>();
    private readonly IReportAttestationStore _store;
    private readonly IAuditTrailService _auditTrail;

    public ReportAttestationService(IReportAttestationStore store, IAuditTrailService auditTrail)
    {
        _store = store;
        _auditTrail = auditTrail;
    }

    public ReportAttestation CreateAttestation(string reportType, string reportPath, DateTime from, DateTime to, string? branch, string? section, string? userId, string? username)
    {
        var hash = ComputeSha256(reportPath);
        var a = new ReportAttestation
        {
            Uuid = Guid.NewGuid().ToString("N"),
            ReportType = reportType,
            ReportPath = reportPath,
            DateFrom = from.ToString("yyyy-MM-dd"),
            DateTo = to.ToString("yyyy-MM-dd"),
            Branch = branch,
            Section = section,
            Sha256Hash = hash,
            Status = AttestationStatus.Generated,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            GeneratedByUserId = userId,
            GeneratedByUsername = username
        };
        _store.Insert(a);
        _log.Information("Report attestation created: {ReportType} {Path}", reportType, reportPath);
        return a;
    }

    public ReportAttestation? GetByReportPath(string reportPath)
    {
        return _store.GetByReportPath(reportPath);
    }

    public bool VerifyHash(string reportPath, string expectedHash)
    {
        if (!File.Exists(reportPath)) return false;
        var actual = ComputeSha256(reportPath);
        return string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase);
    }

    public void RefreshFileHash(string reportPath)
    {
        var a = _store.GetByReportPath(reportPath);
        if (a == null)
        {
            _log.Warning("RefreshFileHash: no attestation for {Path}", reportPath);
            return;
        }
        if (!File.Exists(reportPath))
        {
            _log.Warning("RefreshFileHash: file missing {Path}", reportPath);
            return;
        }
        a.Sha256Hash = ComputeSha256(reportPath);
        _store.Update(a);
        _log.Information("Refreshed attestation hash for {Path}", reportPath);
    }

    public void MarkReviewed(long attestationId, string userId, string username)
    {
        var result = _store.GetResult(attestationId);
        if (!result.IsSuccess)
        {
            _log.Warning("MarkReviewed: could not load attestation {Id}: {Error}", attestationId, result.Error);
            return;
        }
        var a = result.Value!;
        if (a.Status != AttestationStatus.Generated)
        {
            _log.Warning("Attestation {Id} already {Status}", attestationId, a.Status);
            return;
        }
        a.Status = AttestationStatus.Reviewed;
        a.ReviewedAt = DateTime.UtcNow.ToString("O");
        a.ReviewedByUserId = userId;
        a.ReviewedByUsername = username;
        _store.Update(a);
        _ = _auditTrail.LogAsync(AuditAction.ReportReviewed, AuditCategory.Report, "ReportAttestation", a.ReportPath, AttestationStatus.Generated, AttestationStatus.Reviewed, $"Reviewed by {username}", true);
    }

    public void MarkApproved(long attestationId, string userId, string username)
    {
        var result = _store.GetResult(attestationId);
        if (!result.IsSuccess)
        {
            _log.Warning("MarkApproved: could not load attestation {Id}: {Error}", attestationId, result.Error);
            return;
        }
        var a = result.Value!;
        if (a.Status == AttestationStatus.Approved)
        {
            _log.Warning("Attestation {Id} already approved", attestationId);
            return;
        }
        a.Status = AttestationStatus.Approved;
        a.ApprovedAt = DateTime.UtcNow.ToString("O");
        a.ApprovedByUserId = userId;
        a.ApprovedByUsername = username;
        _store.Update(a);
        _ = _auditTrail.LogAsync(AuditAction.ReportApproved, AuditCategory.Report, "ReportAttestation", a.ReportPath, a.Status, AttestationStatus.Approved, $"Approved by {username}", true);
    }

    public static string ComputeSha256(string filePath)
    {
        if (!File.Exists(filePath)) return "";
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
