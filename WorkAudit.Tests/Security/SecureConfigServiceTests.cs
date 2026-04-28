using System;
using FluentAssertions;
using WorkAudit.Core.Security;
using Xunit;

namespace WorkAudit.Tests.Security;

public class SecureConfigServiceTests
{
    private readonly SecureConfigService _service;

    public SecureConfigServiceTests()
    {
        _service = new SecureConfigService();
    }

    [Fact]
    public void Encrypt_ShouldReturnEncryptedStringWithPrefix()
    {
        var plaintext = "my-secret-password";

        var encrypted = _service.Encrypt(plaintext);

        encrypted.Should().StartWith("enc:v1:");
        encrypted.Should().NotBe(plaintext);
        encrypted.Length.Should().BeGreaterThan(plaintext.Length);
    }

    [Fact]
    public void Encrypt_EmptyString_ShouldReturnEmpty()
    {
        var encrypted = _service.Encrypt("");

        encrypted.Should().BeEmpty();
    }

    [Fact]
    public void Encrypt_NullString_ShouldReturnEmpty()
    {
#pragma warning disable CS8625
        var encrypted = _service.Encrypt(null);
#pragma warning restore CS8625

        encrypted.Should().BeEmpty();
    }

    [Fact]
    public void Decrypt_ShouldReturnOriginalPlaintext()
    {
        var plaintext = "my-secret-password";
        var encrypted = _service.Encrypt(plaintext);

        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_WithoutPrefix_ShouldReturnOriginalValue()
    {
        var plaintext = "not-encrypted";

        var result = _service.Decrypt(plaintext);

        result.Should().Be(plaintext);
    }

    [Fact]
    public void Decrypt_NullValue_ShouldReturnNull()
    {
        var result = _service.Decrypt(null);

        result.Should().BeNull();
    }

    [Fact]
    public void Decrypt_EmptyValue_ShouldReturnNull()
    {
        var result = _service.Decrypt("");

        result.Should().BeNull();
    }

    [Fact]
    public void Decrypt_InvalidEncryptedData_ShouldReturnNull()
    {
        var invalid = "enc:v1:invalid-base64-data";

        var result = _service.Decrypt(invalid);

        result.Should().BeNull();
    }

    [Fact]
    public void IsEncrypted_WithPrefix_ShouldReturnTrue()
    {
        var encrypted = _service.Encrypt("test");

        var isEncrypted = _service.IsEncrypted(encrypted);

        isEncrypted.Should().BeTrue();
    }

    [Fact]
    public void IsEncrypted_WithoutPrefix_ShouldReturnFalse()
    {
        var plaintext = "not-encrypted";

        var isEncrypted = _service.IsEncrypted(plaintext);

        isEncrypted.Should().BeFalse();
    }

    [Fact]
    public void IsEncrypted_NullValue_ShouldReturnFalse()
    {
        var isEncrypted = _service.IsEncrypted(null);

        isEncrypted.Should().BeFalse();
    }

    [Fact]
    public void Encrypt_SameValueTwice_ShouldProduceDifferentCiphertext()
    {
        var plaintext = "test-password";

        var encrypted1 = _service.Encrypt(plaintext);
        var encrypted2 = _service.Encrypt(plaintext);

        encrypted1.Should().NotBe(encrypted2);
        _service.Decrypt(encrypted1).Should().Be(plaintext);
        _service.Decrypt(encrypted2).Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_Decrypt_LongString_ShouldWork()
    {
        var longText = new string('A', 1000);

        var encrypted = _service.Encrypt(longText);
        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(longText);
    }

    [Fact]
    public void Encrypt_Decrypt_SpecialCharacters_ShouldWork()
    {
        var special = "!@#$%^&*(){}[]|\\:;\"'<>,.?/~`";

        var encrypted = _service.Encrypt(special);
        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(special);
    }

    [Fact]
    public void Encrypt_Decrypt_UnicodeCharacters_ShouldWork()
    {
        var unicode = "مرحبا العالم 你好世界 🔒🔑";

        var encrypted = _service.Encrypt(unicode);
        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(unicode);
    }
}
