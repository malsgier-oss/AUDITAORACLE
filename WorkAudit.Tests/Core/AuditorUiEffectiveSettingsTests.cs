using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using WorkAudit.Core.Services;
using WorkAudit.Domain;
using WorkAudit.Storage;
using Xunit;

namespace WorkAudit.Tests.Core;

public class AuditorUiEffectiveSettingsTests
{
    [Fact]
    public void GetWebcamBool_NonAuditor_IgnoresOverrides()
    {
        var store = new Mock<IConfigStore>();
        store.Setup(s => s.GetSettingBool("enable_auto_capture", false)).Returns(true);
        var partial = new JObject { ["enable_auto_capture"] = false };
        AuditorUiEffectiveSettings.GetWebcamBool(Roles.Manager, partial, store.Object, "enable_auto_capture", false)
            .Should().BeTrue();
    }

    [Fact]
    public void GetWebcamBool_Auditor_WithOverride_UsesOverride()
    {
        var store = new Mock<IConfigStore>();
        store.Setup(s => s.GetSettingBool("enable_auto_capture", false)).Returns(false);
        var partial = new JObject { ["enable_auto_capture"] = true };
        AuditorUiEffectiveSettings.GetWebcamBool(Roles.Auditor, partial, store.Object, "enable_auto_capture", false)
            .Should().BeTrue();
    }

    [Fact]
    public void GetWebcamBool_Auditor_NoKey_UsesGlobal()
    {
        var store = new Mock<IConfigStore>();
        store.Setup(s => s.GetSettingBool("enable_auto_capture", false)).Returns(true);
        var partial = new JObject();
        AuditorUiEffectiveSettings.GetWebcamBool(Roles.Auditor, partial, store.Object, "enable_auto_capture", false)
            .Should().BeTrue();
    }

    [Fact]
    public void GetWebcamInt_Auditor_WithOverride_UsesOverride()
    {
        var store = new Mock<IConfigStore>();
        store.Setup(s => s.GetSettingInt("auto_capture_cooldown_seconds", 8)).Returns(8);
        var partial = new JObject { ["auto_capture_cooldown_seconds"] = 12 };
        AuditorUiEffectiveSettings.GetWebcamInt(Roles.Auditor, partial, store.Object, "auto_capture_cooldown_seconds", 8)
            .Should().Be(12);
    }

    [Fact]
    public void SetShortcutMap_PreservesExistingWebcamKeys()
    {
        var root = new JObject { ["enable_auto_capture"] = true };
        AuditorUiPreferencesJson.SetShortcutMap(root,
            new Dictionary<string, string> { [KeyboardShortcutIds.ProcessingMerge] = "None|B" });
        root["enable_auto_capture"]!.Value<bool>().Should().BeTrue();
        root[AuditorUiPreferencesJson.KeyboardShortcutsKey]!.Type.Should().Be(JTokenType.Object);
    }

    [Fact]
    public void MergeWebcamFields_KeepsKeyboardShortcuts()
    {
        var root = new JObject();
        AuditorUiPreferencesJson.SetShortcutMap(root,
            new Dictionary<string, string> { [KeyboardShortcutIds.ProcessingRefresh] = "None|Q" });
        AuditorUiPreferencesJson.MergeWebcamFields(root, true, false, 10, false, true);
        root[AuditorUiPreferencesJson.KeyboardShortcutsKey]!.Type.Should().Be(JTokenType.Object);
        root[AuditorUiPreferencesJson.EnableAutoCapture]!.Value<bool>().Should().BeTrue();
        root[AuditorUiPreferencesJson.AutoCaptureCooldownSeconds]!.Value<int>().Should().Be(10);
    }
}
