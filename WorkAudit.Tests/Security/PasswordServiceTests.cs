using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using WorkAudit.Core.Security;
using Xunit;

namespace WorkAudit.Tests.Security;

public class PasswordServiceTests
{
    private readonly PasswordService _service;

    public PasswordServiceTests()
    {
        _service = new PasswordService();
    }

    [Fact]
    public void HashPassword_ShouldReturnBCryptHash()
    {
        var password = "MySecure123!";

        var hash = _service.HashPassword(password);

        hash.Should().NotBeNullOrEmpty();
        hash.Should().StartWith("$2");
        hash.Length.Should().Be(60);
    }

    [Fact]
    public void HashPassword_EmptyPassword_ShouldThrowException()
    {
        Action act = () => _service.HashPassword("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HashPassword_NullPassword_ShouldThrowException()
    {
        Action act = () => _service.HashPassword(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HashPassword_SamePasswordTwice_ShouldProduceDifferentHashes()
    {
        var password = "MyPassword123!";

        var hash1 = _service.HashPassword(password);
        var hash2 = _service.HashPassword(password);

        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ShouldReturnTrue()
    {
        var password = "MySecure123!";
        var hash = _service.HashPassword(password);

        var result = _service.VerifyPassword(password, hash);

        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_IncorrectPassword_ShouldReturnFalse()
    {
        var password = "MySecure123!";
        var hash = _service.HashPassword(password);

        var result = _service.VerifyPassword("WrongPassword", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_EmptyPassword_ShouldReturnFalse()
    {
        var hash = _service.HashPassword("test");

        var result = _service.VerifyPassword("", hash);

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_EmptyHash_ShouldReturnFalse()
    {
        var result = _service.VerifyPassword("password", "");

        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_InvalidHash_ShouldReturnFalse()
    {
        var result = _service.VerifyPassword("password", "invalid-hash");

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidatePasswordStrength_ValidPassword_ShouldReturnTrue()
    {
        var password = "SecurePass123!";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeTrue();
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidatePasswordStrength_TooShort_ShouldReturnFalse()
    {
        var password = "Abc1!";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("8 characters"));
    }

    [Fact]
    public void ValidatePasswordStrength_NoUppercase_ShouldReturnFalse()
    {
        var password = "lowercase123!";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("uppercase"));
    }

    [Fact]
    public void ValidatePasswordStrength_NoLowercase_ShouldReturnFalse()
    {
        var password = "UPPERCASE123!";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("lowercase"));
    }

    [Fact]
    public void ValidatePasswordStrength_NoDigit_ShouldReturnFalse()
    {
        var password = "NoDigitsHere!";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("number"));
    }

    [Fact]
    public void ValidatePasswordStrength_NoSpecialChar_ShouldReturnFalse()
    {
        var password = "NoSpecial123";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("special character"));
    }

    [Fact]
    public void ValidatePasswordStrength_TooLong_ShouldReturnFalse()
    {
        var password = new string('A', 129) + "1!";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Should().Contain(e => e.Contains("128 characters"));
    }

    [Fact]
    public void ValidatePasswordStrength_EmptyPassword_ShouldReturnFalse()
    {
        var isValid = _service.ValidatePasswordStrength("", out var errors);

        isValid.Should().BeFalse();
        errors.Should().NotBeEmpty();
    }

    [Fact]
    public void ValidatePasswordStrength_MultipleErrors_ShouldListAll()
    {
        var password = "weak";

        var isValid = _service.ValidatePasswordStrength(password, out var errors);

        isValid.Should().BeFalse();
        errors.Count.Should().BeGreaterThan(1);
    }

    [Fact]
    public void GenerateTemporaryPassword_ShouldBeValid()
    {
        var tempPassword = _service.GenerateTemporaryPassword();

        tempPassword.Should().NotBeNullOrEmpty();
        tempPassword.Length.Should().BeGreaterThanOrEqualTo(8);
        
        var isValid = _service.ValidatePasswordStrength(tempPassword, out var errors);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void GenerateTemporaryPassword_ShouldBeUnique()
    {
        var temp1 = _service.GenerateTemporaryPassword();
        var temp2 = _service.GenerateTemporaryPassword();

        temp1.Should().NotBe(temp2);
    }

    [Fact]
    public void GenerateEmergencyAccessCodes_ShouldReturnTenDistinctFormattedCodes()
    {
        var codes = _service.GenerateEmergencyAccessCodes(10);

        codes.Should().HaveCount(10);
        codes.Distinct().Should().HaveCount(10);
        codes.Should().OnlyContain(c => c.Length == 14 && c[4] == '-' && c[9] == '-');
    }

    [Fact]
    public void HashPassword_And_Verify_Integration()
    {
        var password = "IntegrationTest123!";

        var hash = _service.HashPassword(password);
        var verify = _service.VerifyPassword(password, hash);

        verify.Should().BeTrue();
    }
}
