using FluentAssertions;
using VirtaMatePackageManager.Core.Services.Validation;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.Tests.Services.Validation;

/// <summary>
/// Unit tests for VarFileValidationService.
/// </summary>
public class VarFileValidationServiceTests
{
    private readonly VarFileValidationService _service;

    public VarFileValidationServiceTests()
    {
        _service = new VarFileValidationService();
    }

    [Fact]
    public void ValidateVarFileName_ValidFormat_ReturnsSuccess()
    {
        // Arrange
        var filePath = "C:\\Vars\\Creator.Package.1.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.ParsedName.Should().NotBeNull();
        result.ParsedName!.Creator.Should().Be("Creator");
        result.ParsedName.Package.Should().Be("Package");
        result.ParsedName.Version.Should().Be("1");
        result.ParsedName.IsLatest.Should().BeFalse();
    }

    [Fact]
    public void ValidateVarFileName_ValidLatestVersion_ReturnsSuccess()
    {
        // Arrange
        var filePath = "Creator.Package.latest.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ParsedName!.IsLatest.Should().BeTrue();
        result.ParsedName.Version.Should().Be("latest");
    }

    [Fact]
    public void ValidateVarFileName_InvalidExtension_ReturnsFailure()
    {
        // Arrange
        var filePath = "Creator.Package.1.zip";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain(".var");
    }

    [Fact]
    public void ValidateVarFileName_InvalidFormat_ReturnsFailure()
    {
        // Arrange
        var filePath = "Invalid.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("3 parts");
    }

    [Fact]
    public void ValidateVarFileName_EmptyPath_ReturnsFailure()
    {
        // Arrange
        var filePath = string.Empty;

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("empty");
    }

    [Fact]
    public void ValidateVarFileName_InvalidCreatorName_ReturnsFailure()
    {
        // Arrange
        var filePath = "Creator-Name.Package.1.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Creator name");
    }

    [Fact]
    public void ValidateVarFileName_InvalidPackageName_ReturnsFailure()
    {
        // Arrange
        var filePath = "Creator.Package-Name.1.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("Package name");
    }

    [Fact]
    public void ValidateVarFileName_ValidWithUnderscore_ReturnsSuccess()
    {
        // Arrange
        var filePath = "Creator_Name.Package_Name.1.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ParsedName!.Creator.Should().Be("Creator_Name");
        result.ParsedName.Package.Should().Be("Package_Name");
    }

    [Fact]
    public void ValidateVarFileName_CreatorNameTooLong_ReturnsFailure()
    {
        // Arrange
        var longName = new string('A', 61);
        var filePath = $"{longName}.Package.1.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("1-60 characters");
    }

    [Fact]
    public void ValidateVarFileName_PackageNameTooLong_ReturnsFailure()
    {
        // Arrange
        var longName = new string('A', 81);
        var filePath = $"Creator.{longName}.1.var";

        // Act
        var result = _service.ValidateVarFileName(filePath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Error.Should().Contain("1-80 characters");
    }
}

