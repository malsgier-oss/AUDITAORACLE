using System.IO;
using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

/// <summary>
/// Application-level immutability for archived documents.
/// NOT hardware-certified WORM - provides tamper detection via hash verification.
/// Administrators with file system access can still modify files; this service detects such tampering.
/// </summary>
public interface IImmutabilityService
{
    /// <summary>Compute SHA256 hash of file. Returns hex string.</summary>
    string? ComputeHash(string filePath);

    /// <summary>Make document immutable: compute hash, set read-only, update DB.</summary>
    bool MakeImmutable(Document doc);

    /// <summary>Verify file hash matches stored hash. Returns true if match, false if tampered. Logs failure to audit.</summary>
    bool VerifyHash(Document doc);
}

public class ImmutabilityService : IImmutabilityService
{
    private readonly ILogger _log = LoggingService.ForContext<ImmutabilityService>();
    private readonly IDocumentStore _documentStore;
    private readonly IAuditTrailService _auditTrail;

    public ImmutabilityService(IDocumentStore documentStore, IAuditTrailService auditTrail)
    {
        _documentStore = documentStore;
        _auditTrail = auditTrail;
    }

    public string? ComputeHash(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to compute hash for {Path}", filePath);
            return null;
        }
    }

    public bool MakeImmutable(Document doc)
    {
        if (string.IsNullOrEmpty(doc.FilePath) || !File.Exists(doc.FilePath))
        {
            _log.Warning("Cannot make immutable: file not found {Path}", doc.FilePath);
            return false;
        }

        var hash = ComputeHash(doc.FilePath);
        if (string.IsNullOrEmpty(hash))
            return false;

        try
        {
            var attrs = File.GetAttributes(doc.FilePath);
            File.SetAttributes(doc.FilePath, attrs | FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to set read-only on {Path}", doc.FilePath);
            // Continue - we still store the hash for verification
        }

        doc.IsImmutable = true;
        doc.ImmutableHash = hash;
        doc.ImmutableSince = DateTime.UtcNow.ToString("O");
        doc.HashVerificationCount = 0;

        return _documentStore.Update(doc);
    }

    public bool VerifyHash(Document doc)
    {
        if (!doc.IsImmutable || string.IsNullOrEmpty(doc.ImmutableHash) || string.IsNullOrEmpty(doc.FilePath))
            return true; // Not immutable, nothing to verify

        if (!File.Exists(doc.FilePath))
        {
            _log.Warning("File not found for hash verification: {Path}", doc.FilePath);
            _ = _auditTrail.LogDocumentActionAsync(AuditAction.HashVerificationFailed, doc,
                $"File not found. Expected hash: {doc.ImmutableHash}");
            return false;
        }

        var currentHash = ComputeHash(doc.FilePath);
        if (string.IsNullOrEmpty(currentHash))
            return false;

        if (!string.Equals(currentHash, doc.ImmutableHash, StringComparison.OrdinalIgnoreCase))
        {
            _log.Warning("Hash mismatch for document {Id}: expected {Expected}, got {Actual}",
                doc.Id, doc.ImmutableHash, currentHash);
            _ = _auditTrail.LogDocumentActionAsync(AuditAction.HashVerificationFailed, doc,
                $"Tamper detected. Expected: {doc.ImmutableHash}, Actual: {currentHash}");

            doc.LastHashVerification = DateTime.UtcNow.ToString("O");
            _documentStore.Update(doc);
            return false;
        }

        doc.HashVerificationCount++;
        doc.LastHashVerification = DateTime.UtcNow.ToString("O");
        _documentStore.Update(doc);
        return true;
    }
}
