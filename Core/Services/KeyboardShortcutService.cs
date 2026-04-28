using System.Text;
using System.Windows.Input;
using WorkAudit.Config;

namespace WorkAudit.Core.Services;

/// <summary>Persists shortcuts under UserSettings key <c>keyboard_shortcuts</c> as commandId -> <c>Mods|KeyName</c>.</summary>
public sealed class KeyboardShortcutService : IKeyboardShortcutService
{
    private const string UserSettingsKey = "keyboard_shortcuts";

    private static readonly Dictionary<string, (Key Key, ModifierKeys Mods)> Defaults = new()
    {
        [KeyboardShortcutIds.ProcessingMerge] = (Key.A, ModifierKeys.None),
        [KeyboardShortcutIds.ProcessingMergeAlternate] = (Key.M, ModifierKeys.Control),
        [KeyboardShortcutIds.ProcessingRefresh] = (Key.Q, ModifierKeys.None),
        [KeyboardShortcutIds.ProcessingSelectAllChecks] = (Key.F, ModifierKeys.None),
        [KeyboardShortcutIds.ProcessingClearChecks] = (Key.D, ModifierKeys.None),
        [KeyboardShortcutIds.ProcessingUncheckRow] = (Key.S, ModifierKeys.None),
        [KeyboardShortcutIds.ProcessingGridSelectAll] = (Key.A, ModifierKeys.Control),
        [KeyboardShortcutIds.ProcessingSetTypeSection] = (Key.Enter, ModifierKeys.None),
        [KeyboardShortcutIds.ProcessingDeleteSelected] = (Key.Delete, ModifierKeys.None),
    };

    private Dictionary<string, (Key Key, ModifierKeys Mods)> _effective = new();

    public KeyboardShortcutService()
    {
        Reload();
    }

    public void Reload()
    {
        _effective = new Dictionary<string, (Key, ModifierKeys)>(Defaults);
        var raw = UserSettings.Get<Dictionary<string, string>>(UserSettingsKey, null);
        if (raw == null) return;
        foreach (var kv in raw)
        {
            if (!Defaults.ContainsKey(kv.Key)) continue;
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            if (TryDeserialize(kv.Value, out var k, out var m))
                _effective[kv.Key] = (k, m);
        }
    }

    public bool Matches(KeyEventArgs e, string commandId)
    {
        if (!TryGetBinding(commandId, out var key, out var mods)) return false;
        if (e.Key != key) return false;
        return NormalizeMods(Keyboard.Modifiers) == NormalizeMods(mods);
    }

    public string GetDisplayString(string commandId)
    {
        if (!TryGetBinding(commandId, out var key, out var mods)) return "";
        return FormatDisplay(key, mods);
    }

    public string GetDisplayStringFromSerialized(string serialized)
    {
        if (!TryDeserialize(serialized, out var key, out var mods)) return serialized;
        return FormatDisplay(key, mods);
    }

    private static string FormatDisplay(Key key, ModifierKeys mods)
    {
        var sb = new StringBuilder();
        if (mods.HasFlag(ModifierKeys.Control)) sb.Append("Ctrl+");
        if (mods.HasFlag(ModifierKeys.Alt)) sb.Append("Alt+");
        if (mods.HasFlag(ModifierKeys.Shift)) sb.Append("Shift+");
        sb.Append(KeyToDisplayName(key));
        return sb.ToString();
    }

    public string? GetSerializedOrDefault(string commandId)
    {
        if (!Defaults.TryGetValue(commandId, out var def)) return null;
        var raw = UserSettings.Get<Dictionary<string, string>>(UserSettingsKey, null);
        if (raw != null && raw.TryGetValue(commandId, out var s) && !string.IsNullOrWhiteSpace(s))
            return s;
        return Serialize(def.Key, def.Mods);
    }

    public IReadOnlyDictionary<string, string> GetDefaultSerializedMap()
    {
        var d = new Dictionary<string, string>();
        foreach (var kv in Defaults)
            d[kv.Key] = Serialize(kv.Value.Key, kv.Value.Mods);
        return d;
    }

    public string? TrySaveOverrides(IReadOnlyDictionary<string, string> serializedByCommandId)
    {
        var err = ValidateSerializedMap(serializedByCommandId);
        if (err != null) return err;
        UserSettings.Set(UserSettingsKey, serializedByCommandId.ToDictionary(kv => kv.Key, kv => kv.Value));
        Reload();
        return null;
    }

    public string? ValidateSerializedMap(IReadOnlyDictionary<string, string> map)
    {
        var resolved = new Dictionary<string, (Key Key, ModifierKeys Mods)>();
        foreach (var id in KeyboardShortcutIds.All)
        {
            if (!Defaults.TryGetValue(id, out var def)) continue;
            if (!map.TryGetValue(id, out var s) || string.IsNullOrWhiteSpace(s))
            {
                resolved[id] = def;
                continue;
            }
            if (!TryDeserialize(s, out var k, out var m))
                return $"Invalid shortcut for {id}: {s}";
            resolved[id] = (k, m);
        }

        var seen = new Dictionary<(Key, ModifierKeys), string>();
        foreach (var kv in resolved)
        {
            var tuple = (kv.Value.Key, NormalizeMods(kv.Value.Mods));
            if (seen.TryGetValue(tuple, out var other))
                return $"Duplicate shortcut {GetDisplayStringForBinding(kv.Value.Key, kv.Value.Mods)} for '{other}' and '{kv.Key}'.";
            seen[tuple] = kv.Key;
        }

        return null;
    }

    private bool TryGetBinding(string commandId, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        if (_effective.TryGetValue(commandId, out var b))
        {
            key = b.Key;
            mods = b.Mods;
            return true;
        }
        return false;
    }

    private static string KeyToDisplayName(Key key) => key switch
    {
        Key.Return => "Enter",
        Key.Back => "Backspace",
        _ => key.ToString()
    };

    private static string GetDisplayStringForBinding(Key key, ModifierKeys mods) => FormatDisplay(key, mods);

    public static string Serialize(Key key, ModifierKeys mods) =>
        $"{SerializeMods(mods)}|{key}";

    private static bool TryDeserialize(string s, out Key key, out ModifierKeys mods)
    {
        key = Key.None;
        mods = ModifierKeys.None;
        var pipe = s.LastIndexOf('|');
        if (pipe <= 0 || pipe >= s.Length - 1) return false;
        var modPart = s[..pipe].Trim();
        var keyPart = s[(pipe + 1)..].Trim();
        mods = ParseMods(modPart);
        return Enum.TryParse(keyPart, true, out key);
    }

    private static string SerializeMods(ModifierKeys m)
    {
        m &= ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows;
        if (m == ModifierKeys.None) return "None";
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Control");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");
        return string.Join("+", parts);
    }

    private static ModifierKeys ParseMods(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Equals("None", StringComparison.OrdinalIgnoreCase))
            return ModifierKeys.None;
        ModifierKeys m = ModifierKeys.None;
        foreach (var part in s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Equals("Control", StringComparison.OrdinalIgnoreCase)) m |= ModifierKeys.Control;
            else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase)) m |= ModifierKeys.Alt;
            else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase)) m |= ModifierKeys.Shift;
            else if (part.Equals("Windows", StringComparison.OrdinalIgnoreCase)) m |= ModifierKeys.Windows;
        }
        return m;
    }

    private static ModifierKeys NormalizeMods(ModifierKeys m) =>
        m & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Windows);
}
