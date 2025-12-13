using FluentAssertions;
using VirtaMatePackageManager.Core.Services.Parsing;
using VirtaMatePackageManager.Core.ValueObjects.Validation;

namespace VirtaMatePackageManager.Core.Tests.Services.Parsing;

/// <summary>
/// Unit tests for VarFileParsingService.
/// </summary>
public class VarFileParsingServiceTests
{
    private readonly VarFileParsingService _service;

    public VarFileParsingServiceTests()
    {
        _service = new VarFileParsingService();
    }

    [Fact]
    public void ParseVarFileName_ValidNumericVersion_ReturnsParsedComponents()
    {
        // Arrange
        var fileName = "Creator.Package.1.var";

        // Act
        var result = _service.ParseVarFileName(fileName);

        // Assert
        result.Should().NotBeNull();
        result.Creator.Should().Be("Creator");
        result.Package.Should().Be("Package");
        result.Version.Should().Be("1");
        result.IsLatest.Should().BeFalse();
        result.FullVarName.Should().Be("Creator.Package.1.var");
    }

    [Fact]
    public void ParseVarFileName_ValidLatestVersion_ReturnsParsedComponents()
    {
        // Arrange
        var fileName = "Creator.Package.latest.var";

        // Act
        var result = _service.ParseVarFileName(fileName);

        // Assert
        result.Should().NotBeNull();
        result.Creator.Should().Be("Creator");
        result.Package.Should().Be("Package");
        result.Version.Should().Be("latest");
        result.IsLatest.Should().BeTrue();
    }

    [Fact]
    public void ParseVarFileName_WithoutExtension_ThrowsFormatException()
    {
        // Arrange - VAR filenames should always have .var extension
        var fileName = "Creator.Package.5";

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => _service.ParseVarFileName(fileName));
        exception.Message.Should().Contain("Expected Creator.Package.Version");
    }

    [Fact]
    public void ParseVarFileName_InvalidFormat_ThrowsFormatException()
    {
        // Arrange
        var fileName = "InvalidFormat.var";

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => _service.ParseVarFileName(fileName));
        exception.Message.Should().Contain("Expected Creator.Package.Version");
    }

    [Fact]
    public void ParseVarFileName_EmptyFileName_ThrowsArgumentException()
    {
        // Arrange
        var fileName = string.Empty;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _service.ParseVarFileName(fileName));
        exception.ParamName.Should().Be("fileName");
    }

    [Fact]
    public void ParseVarFileName_NullFileName_ThrowsArgumentException()
    {
        // Arrange
        string? fileName = null;

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _service.ParseVarFileName(fileName!));
        exception.ParamName.Should().Be("fileName");
    }

    [Fact]
    public void ParseVarFileName_InvalidVersion_ThrowsFormatException()
    {
        // Arrange
        var fileName = "Creator.Package.invalid.var";

        // Act & Assert
        var exception = Assert.Throws<FormatException>(() => _service.ParseVarFileName(fileName));
        exception.Message.Should().Contain("Invalid version format");
    }

    [Fact]
    public void ParseVarFileName_ZeroVersion_ThrowsArgumentException()
    {
        // Arrange
        var fileName = "Creator.Package.0.var";

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => _service.ParseVarFileName(fileName));
        exception.Message.Should().Contain("Version must be >= 1");
    }

    [Fact]
    public void ParseVarFileName_CaseInsensitiveLatest_ReturnsParsedComponents()
    {
        // Arrange
        var fileName = "Creator.Package.LATEST.var";

        // Act
        var result = _service.ParseVarFileName(fileName);

        // Assert
        result.Should().NotBeNull();
        result.IsLatest.Should().BeTrue();
        result.Version.Should().Be("latest");
    }
}

