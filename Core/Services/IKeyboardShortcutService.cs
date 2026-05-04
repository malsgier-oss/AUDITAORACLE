using System.Windows.Input;

namespace WorkAudit.Core.Services;

/// <summary>User-configurable Processing keyboard shortcuts (Oracle for Auditors, <see cref="Config.UserSettings"/> for other roles).</summary>
public interface IKeyboardShortcutService
{
    /// <summary>Reload bindings from user settings (call after Control Panel save).</summary>
    void Reload();

    /// <summary>Whether the key event matches the binding for <paramref name="commandId"/>.</summary>
    bool Matches(KeyEventArgs e, string commandId);

    /// <summary>Human-readable shortcut for tooltips, e.g. "Ctrl+M", "Q".</summary>
    string GetDisplayString(string commandId);

    /// <summary>Current serialized value for the command, or null if using default.</summary>
    string? GetSerializedOrDefault(string commandId);

    /// <summary>Persist overrides and reload. Returns null if OK, or error message (e.g. duplicate bindings).</summary>
    string? TrySaveOverrides(IReadOnlyDictionary<string, string> serializedByCommandId);

    /// <summary>Default serialized map for reset.</summary>
    IReadOnlyDictionary<string, string> GetDefaultSerializedMap();

    /// <summary>Validate map without saving. Returns error message or null.</summary>
    string? ValidateSerializedMap(IReadOnlyDictionary<string, string> map);

    /// <summary>Display string for a stored <c>Mods|Key</c> value (e.g. for Control Panel after capture).</summary>
    string GetDisplayStringFromSerialized(string serialized);
}
