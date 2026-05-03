using System.Security.Cryptography;
using Serilog;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;

namespace WorkAudit.Core.Security;

/// <summary>
/// Service for managing user sessions.
/// Handles login, logout, session validation, and inactivity timeout.
/// </summary>
public interface ISessionService
{
    Session? CurrentSession { get; }
    User? CurrentUser { get; }
    bool IsAuthenticated { get; }
    int InactivityTimeoutMinutes { get; }

    void RecordActivity();
    Task<bool> CheckInactivityTimeoutAsync();

    Task<(bool Success, string? Error, Session? Session)> LoginAsync(string username, string password);
    Task<bool> LogoutAsync();
    Task<bool> ValidateSessionAsync(string token);
    Task<Session?> GetSessionAsync(string token);
    void SetCurrentSession(Session? session, User? user);
}

public class SessionService : ISessionService
{
    private readonly ILogger _log = LoggingService.ForContext<SessionService>();
    private readonly IUserStore _userStore;
    private readonly IPasswordService _passwordService;
    private readonly IAuditTrailService _auditTrail;
    private readonly object _sessionLock = new();

    private const int SessionExpiryHours = 8;
    private const int MaxFailedAttempts = 5;
    private const int LockoutMinutes = 30;

    private DateTime _lastActivityAt = DateTime.MinValue;
    private Session? _currentSession;
    private User? _currentUser;

    public Session? CurrentSession { get { lock (_sessionLock) { return _currentSession; } } }
    public User? CurrentUser { get { lock (_sessionLock) { return _currentUser; } } }
    public bool IsAuthenticated { get { lock (_sessionLock) { return _currentSession != null && _currentUser != null; } } }
    public int InactivityTimeoutMinutes { get; } = 30;

    public void RecordActivity()
    {
        lock (_sessionLock)
        {
            if (_currentSession != null && _currentUser != null)
                _lastActivityAt = DateTime.UtcNow;
        }
    }

    public async Task<bool> CheckInactivityTimeoutAsync()
    {
        Session? session;
        User? user;
        DateTime lastActivity;
        lock (_sessionLock)
        {
            if (_currentSession == null || _currentUser == null)
                return false;
            if (_lastActivityAt == DateTime.MinValue)
                _lastActivityAt = DateTime.UtcNow;
            lastActivity = _lastActivityAt;
            session = _currentSession;
            user = _currentUser;
        }

        // Re-validate user from DB: if deactivated or locked, force logout immediately
        var currentUserFromDb = _userStore.GetById(session.UserId);
        if (currentUserFromDb == null || !currentUserFromDb.IsActive || currentUserFromDb.IsLocked)
        {
            _log.Information("Session ended: user deactivated or locked: {Username}", user?.Username);
            _userStore.InvalidateSession(session.Token);
            await _auditTrail.LogAsync(AuditAction.SessionExpired, AuditCategory.Authentication,
                "Session", session.Token, details: "User account deactivated or locked");

            lock (_sessionLock)
            {
                _currentSession = null;
                _currentUser = null;
            }
            return true;
        }

        var elapsed = DateTime.UtcNow - lastActivity;
        if (elapsed.TotalMinutes < InactivityTimeoutMinutes)
            return false;

        _log.Information("Session expired due to inactivity: {Username}", user?.Username);
        _userStore.InvalidateSession(session.Token);
        await _auditTrail.LogAsync(AuditAction.SessionExpired, AuditCategory.Authentication,
            "Session", session.Token, details: "Session expired due to inactivity");

        lock (_sessionLock)
        {
            _currentSession = null;
            _currentUser = null;
        }
        return true;
    }

    public SessionService(IUserStore userStore, IPasswordService passwordService, IAuditTrailService auditTrail)
    {
        _userStore = userStore;
        _passwordService = passwordService;
        _auditTrail = auditTrail;
    }

    public async Task<(bool Success, string? Error, Session? Session)> LoginAsync(string username, string password)
    {
        _log.Information("Login attempt for user: {Username}", username);

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return (false, "Username and password are required.", null);
        }

        var user = _userStore.GetByUsername(username);
        if (user == null)
        {
            _log.Warning("Login failed: User not found: {Username}", username);
            await _auditTrail.LogAsync(AuditAction.LoginFailed, AuditCategory.Authentication,
                "User", null, details: $"Unknown user: {username}");
            return (false, "Invalid username or password.", null);
        }

        if (!user.IsActive)
        {
            _log.Warning("Login failed: Account inactive: {Username}", username);
            return (false, "Account is inactive.", null);
        }

        var isAdministrator = user.Role == Roles.Administrator;

        if (!isAdministrator && user.IsLocked)
        {
            _log.Warning("Login failed: Account locked: {Username}", username);
            await _auditTrail.LogAsync(AuditAction.LoginFailed, AuditCategory.Authentication,
                "User", user.Uuid, details: "Account is locked");
            return (false, "Account is locked. Contact an administrator.", null);
        }

        if (_passwordService.VerifyPassword(password, user.PasswordHash))
            return await FinishSuccessfulLoginAsync(user, usedEmergencyCode: false);

        if (isAdministrator)
        {
            var unusedCodes = _userStore.GetUnusedEmergencyCodeHashes(user.Id) ?? Array.Empty<(int Id, string Hash)>();
            foreach (var (codeId, hash) in unusedCodes)
            {
                if (!_passwordService.VerifyPassword(password, hash))
                    continue;
                if (!_userStore.MarkEmergencyCodeUsed(codeId, user.Id))
                    continue;

                user.MustChangePassword = true;
                return await FinishSuccessfulLoginAsync(user, usedEmergencyCode: true);
            }
        }

        user.FailedLoginAttempts++;

        if (user.FailedLoginAttempts >= MaxFailedAttempts)
        {
            user.IsLocked = true;
            _log.Warning("Account locked after {Attempts} failed attempts: {Username}",
                user.FailedLoginAttempts, username);
        }

        _userStore.Update(user);
        await _auditTrail.LogAsync(AuditAction.LoginFailed, AuditCategory.Authentication,
            "User", user.Uuid, details: $"Invalid password (attempt {user.FailedLoginAttempts})");

        return (false, "Invalid username or password.", null);
    }

    private async Task<(bool Success, string? Error, Session? Session)> FinishSuccessfulLoginAsync(User user, bool usedEmergencyCode)
    {
        user.FailedLoginAttempts = 0;
        user.IsLocked = false;
        user.LastLoginAt = DateTime.UtcNow.ToString("O");

        var session = new Session
        {
            Token = GenerateSecureToken(),
            UserId = user.Id,
            Username = user.Username,
            UserRole = user.Role,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            ExpiresAt = DateTime.UtcNow.AddHours(SessionExpiryHours).ToString("O"),
            IsActive = true
        };

        _userStore.Update(user);
        _userStore.CreateSession(session);

        lock (_sessionLock)
        {
            _currentSession = session;
            _currentUser = user;
            _lastActivityAt = DateTime.UtcNow;
        }

        if (usedEmergencyCode)
        {
            _log.Information("Login successful via emergency code: {Username} ({Role})", user.Username, user.Role);
            await _auditTrail.LogAsync(AuditAction.LoginEmergencyCode, AuditCategory.Authentication,
                "User", user.Uuid, details: "Administrator emergency access code accepted");
        }
        else
        {
            _log.Information("Login successful: {Username} ({Role})", user.Username, user.Role);
            await _auditTrail.LogAsync(AuditAction.Login, AuditCategory.Authentication,
                "User", user.Uuid, details: "Login successful");
        }

        return (true, null, session);
    }

    public async Task<bool> LogoutAsync()
    {
        string? sessionToken;
        string? username;
        lock (_sessionLock)
        {
            if (_currentSession == null)
                return true;
            sessionToken = _currentSession.Token;
            username = _currentUser?.Username;
        }

        _userStore.InvalidateSession(sessionToken!);

        await _auditTrail.LogAsync(AuditAction.Logout, AuditCategory.Authentication,
            "Session", sessionToken!, details: $"User logged out: {username}");

        lock (_sessionLock)
        {
            _currentSession = null;
            _currentUser = null;
            _lastActivityAt = DateTime.MinValue;
        }

        _log.Information("Logout successful: {Username}", username);
        return true;
    }

    public async Task<bool> ValidateSessionAsync(string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        var session = _userStore.GetSession(token);
        if (session == null || !session.IsActive)
            return false;

        // Check expiry
        if (DateTime.TryParse(session.ExpiresAt, out var expiry) && expiry < DateTime.UtcNow)
        {
            _userStore.InvalidateSession(token);
            await _auditTrail.LogAsync(AuditAction.SessionExpired, AuditCategory.Authentication,
                "Session", token, details: "Session expired");
            return false;
        }

        // User must still be active and not locked
        var user = _userStore.GetById(session.UserId);
        if (user == null || !user.IsActive || user.IsLocked)
        {
            _userStore.InvalidateSession(token);
            await _auditTrail.LogAsync(AuditAction.SessionExpired, AuditCategory.Authentication,
                "Session", token, details: "User account deactivated or locked");
            return false;
        }

        return true;
    }

    public Task<Session?> GetSessionAsync(string token)
    {
        var session = _userStore.GetSession(token);
        return Task.FromResult(session);
    }

    public void SetCurrentSession(Session? session, User? user)
    {
        lock (_sessionLock)
        {
            _currentSession = session;
            _currentUser = user;
        }

        if (session != null && user != null)
        {
            _log.Debug("Session set for user: {Username}", user.Username);
        }
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }
}
