using System;
using FluentAssertions;
using Moq;
using WorkAudit.Core.Security;
using WorkAudit.Domain;
using WorkAudit.Storage;
using WorkAudit.Tests.Fixtures;
using Xunit;

namespace WorkAudit.Tests.Storage;

public class ConfigStoreTests : IClassFixture<OracleTestFixture>
{
    private readonly OracleTestFixture _fx;
    private readonly Mock<ISecureConfigService> _secureConfigMock;
    private ConfigStore _store => new(_fx.ConnectionString!, _secureConfigMock.Object);

    public ConfigStoreTests(OracleTestFixture f)
    {
        _fx = f;
        _secureConfigMock = new Mock<ISecureConfigService>();
        _secureConfigMock.Setup(x => x.Encrypt(It.IsAny<string>())).Returns<string>(s => "enc:v1:" + s);
        _secureConfigMock.Setup(x => x.Decrypt(It.IsAny<string>())).Returns<string>(s => s.Replace("enc:v1:", ""));
        _secureConfigMock.Setup(x => x.IsEncrypted(It.IsAny<string>())).Returns<string>(s => s?.StartsWith("enc:v1:") ?? false);
    }

    [SkippableFact]
    public void SetSetting_And_GetSettingValue_ShouldWork()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "tset_" + Guid.NewGuid().ToString("N");
        var value = "test_value";

        var setResult = _store.SetSetting(key, value);
        var getValue = _store.GetSettingValue(key);

        setResult.Should().BeTrue();
        getValue.Should().Be(value);
    }

    [SkippableFact]
    public void GetSettingValue_NonExistent_ShouldReturnDefault()
    {
        Skip.IfNot(_fx.IsAvailable);
        var value = _store.GetSettingValue("nonexistent_" + Guid.NewGuid().ToString("N"), "default");

        value.Should().Be("default");
    }

    [SkippableFact]
    public void SetSettingInt_And_GetSettingInt_ShouldWork()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "test_int_" + Guid.NewGuid().ToString("N");
        var value = 42;

        _store.SetSettingInt(key, value);
        var result = _store.GetSettingInt(key);

        result.Should().Be(value);
    }

    [SkippableFact]
    public void SetSettingBool_And_GetSettingBool_ShouldWork()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "test_bool_" + Guid.NewGuid().ToString("N");

        _store.SetSettingBool(key, true);
        var result1 = _store.GetSettingBool(key);

        _store.SetSettingBool(key, false);
        var result2 = _store.GetSettingBool(key);

        result1.Should().BeTrue();
        result2.Should().BeFalse();
    }

    [SkippableFact]
    public void GetSettingBool_NonExistent_ShouldReturnDefault()
    {
        Skip.IfNot(_fx.IsAvailable);
        var result = _store.GetSettingBool("nonexistent_" + Guid.NewGuid().ToString("N"), true);

        result.Should().BeTrue();
    }

    [SkippableFact]
    public void SetSecureSetting_ShouldEncryptValue()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "secure_password_" + Guid.NewGuid().ToString("N");
        var plainValue = "my-secret-password";

        _store.SetSecureSetting(key, plainValue);

        var storedValue = _store.GetSettingValue(key);
        storedValue.Should().StartWith("enc:v1:");
    }

    [SkippableFact]
    public void GetSecureSettingValue_ShouldDecryptValue()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "secure_get_" + Guid.NewGuid().ToString("N");
        var plainValue = "my-secret-password";

        _store.SetSecureSetting(key, plainValue);
        var retrievedValue = _store.GetSecureSettingValue(key);

        retrievedValue.Should().Be(plainValue);
    }

    [SkippableFact]
    public void GetSecureSettingValue_NonEncryptedValue_ShouldReturnAsIs()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "plain_value_" + Guid.NewGuid().ToString("N");
        var value = "not-encrypted";

        _store.SetSetting(key, value);
        var retrievedValue = _store.GetSecureSettingValue(key);

        retrievedValue.Should().Be(value);
    }

    [SkippableFact]
    public void DeleteSetting_ShouldRemoveSetting()
    {
        Skip.IfNot(_fx.IsAvailable);
        var key = "to_delete_" + Guid.NewGuid().ToString("N");
        _store.SetSetting(key, "value");

        var deleteResult = _store.DeleteSetting(key);
        var getValue = _store.GetSettingValue(key);

        deleteResult.Should().BeTrue();
        getValue.Should().BeNull();
    }

    [SkippableFact]
    public void InsertDocumentType_ShouldReturnId()
    {
        Skip.IfNot(_fx.IsAvailable);
        var docType = new ConfigDocumentType
        {
            Name = "Test Document " + Guid.NewGuid().ToString("N"),
            Branch = "TestBranch",
            IsActive = true
        };

        var id = _store.InsertDocumentType(docType);

        id.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public void GetDocumentTypes_ShouldReturnActiveOnly()
    {
        Skip.IfNot(_fx.IsAvailable);
        var nActive = "Active_" + Guid.NewGuid().ToString("N");
        var nIna = "Inactive_" + Guid.NewGuid().ToString("N");
        _store.InsertDocumentType(new ConfigDocumentType { Name = nActive, IsActive = true });
        _store.InsertDocumentType(new ConfigDocumentType { Name = nIna, IsActive = false });

        var types = _store.GetDocumentTypes(includeInactive: false);

        types.Should().Contain(t => t.Name == nActive);
        types.Should().NotContain(t => t.Name == nIna);
    }

    [SkippableFact]
    public void UpdateDocumentType_ShouldModifyExisting()
    {
        Skip.IfNot(_fx.IsAvailable);
        var id = _store.InsertDocumentType(new ConfigDocumentType { Name = "Original_" + Guid.NewGuid().ToString("N") });
        var docType = _store.GetDocumentType(id);
        docType!.Name = "Updated";

        var updateResult = _store.UpdateDocumentType(docType);
        var updated = _store.GetDocumentType(id);

        updateResult.Should().BeTrue();
        updated!.Name.Should().Be("Updated");
    }

    [SkippableFact]
    public void InsertBranch_ShouldReturnId()
    {
        Skip.IfNot(_fx.IsAvailable);
        var branch = new ConfigBranch
        {
            Name = "Test Branch " + Guid.NewGuid().ToString("N"),
            Code = "TB" + Guid.NewGuid().ToString("N")[..6],
            IsActive = true
        };

        var id = _store.InsertBranch(branch);

        id.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public void GetBranches_ShouldReturnAll()
    {
        Skip.IfNot(_fx.IsAvailable);
        var b1 = "Branch1_" + Guid.NewGuid().ToString("N");
        var b2 = "Branch2_" + Guid.NewGuid().ToString("N");
        _store.InsertBranch(new ConfigBranch { Name = b1, IsActive = true });
        _store.InsertBranch(new ConfigBranch { Name = b2, IsActive = true });

        var branches = _store.GetBranches();

        branches.Should().Contain(b => b.Name == b1);
        branches.Should().Contain(b => b.Name == b2);
    }

    [SkippableFact]
    public void InsertCategory_ShouldReturnId()
    {
        Skip.IfNot(_fx.IsAvailable);
        var category = new ConfigCategory
        {
            Name = "Test Category " + Guid.NewGuid().ToString("N"),
            IsActive = true
        };

        var id = _store.InsertCategory(category);

        id.Should().BeGreaterThan(0);
    }
}
