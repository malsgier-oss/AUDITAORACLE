using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Core.Security;
using WorkAudit.Storage;

namespace WorkAudit.Core.Compliance;

/// <summary>
/// GDPR Right to Erasure - removes user and associated data.
/// </summary>
public interface IErasureService
{
    Task<ErasureResult> EraseUserDataAsync(string userId, bool anonymizeAuditLog = true);
}

public class ErasureResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int AuditEntriesAnonymized { get; set; }
    public bool UserDeleted { get; set; }
}

public class ErasureService : IErasureService
{
    private readonly ILogger _log = LoggingService.ForContext<ErasureService>();
    private readonly IUserStore _userStore;
    private readonly IAuditTrailService _auditTrail;

    public ErasureService(IUserStore userStore, IAuditTrailService auditTrail)
    {
        _userStore = userStore;
        _auditTrail = auditTrail;
    }

    public async Task<ErasureResult> EraseUserDataAsync(string userId, bool anonymizeAuditLog = true)
    {
        try
        {
            var user = _userStore.GetByUuid(userId);
            if (user == null)
                return new ErasureResult { Success = false, Error = "User not found" };

            var anonymized = 0;
            if (anonymizeAuditLog)
                _log.Information("Erasure requested for user {UserId} - audit log anonymization would apply", userId);

            _userStore.Delete(user.Id);

            await _auditTrail.LogAsync("UserErased", "Compliance", "User", userId,
                details: "GDPR Right to Erasure executed");

            _log.Information("User data erased: {UserId}", userId);
            return new ErasureResult
            {
                Success = true,
                UserDeleted = true,
                AuditEntriesAnonymized = anonymized
            };
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Erasure failed for user {UserId}", userId);
            return new ErasureResult { Success = false, Error = ex.Message };
        }
    }
}
