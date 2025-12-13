using FluentAssertions;
using System.Text.Json;
using VirtaMatePackageManager.Core.Services.Parsing;
using VirtaMatePackageManager.Core.ValueObjects.Metadata;
using Xunit;

namespace VirtaMatePackageManager.Core.Tests.Services.Parsing;

public class DependencyExtractionServiceTests
{
    private readonly DependencyExtractionService _service;

    public DependencyExtractionServiceTests()
    {
        _service = new DependencyExtractionService();
    }

    [Fact]
    public void ExtractDependencies_ValidDependencies_ReturnsList()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {},
                ""Creator2.Package2.latest"": {}
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        result.Should().Contain(d => d.Name == "Creator1.Package1.1");
        result.Should().Contain(d => d.Name == "Creator2.Package2.latest");
    }

    [Fact]
    public void ExtractDependencies_EmptyDependencies_ReturnsEmptyList()
    {
        // Arrange
        var json = @"{ ""dependencies"": {} }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractDependencies_NoDependenciesProperty_ReturnsEmptyList()
    {
        // Arrange
        var json = @"{ ""otherProperty"": ""value"" }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractDependencies_NestedDependencies_FlattensCorrectly()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {
                    ""dependencies"": {
                        ""Creator2.Package2.1"": {},
                        ""Creator3.Package3.latest"": {}
                    }
                }
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().Contain(d => d.Name == "Creator1.Package1.1");
        result.Should().Contain(d => d.Name == "Creator2.Package2.1");
        result.Should().Contain(d => d.Name == "Creator3.Package3.latest");
    }

    [Fact]
    public void ExtractDependencies_WithLicenseType_ExtractsLicense()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {
                    ""licenseType"": ""MIT""
                }
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].LicenseType.Should().Be("MIT");
    }

    [Fact]
    public void ExtractDependencies_WithMissingFlag_ExtractsMissing()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {
                    ""missing"": true
                }
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].IsMissing.Should().BeTrue();
        result[0].IsOptional.Should().BeTrue(); // Missing dependencies are considered optional
    }

    [Fact]
    public void ExtractDependencies_WithOptionalFlag_ExtractsOptional()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {
                    ""optional"": true
                }
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].IsOptional.Should().BeTrue();
    }

    [Fact]
    public void ExtractDependencies_WithPathPrefix_RemovesPrefix()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""some/path/Creator1.Package1.1"": {}
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Creator1.Package1.1");
    }

    [Fact]
    public void ExtractDependencies_DuplicateNames_RemovesDuplicates()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {},
                ""Creator1.Package1.1"": {}
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1); // Duplicates removed
    }

    [Fact]
    public void ExtractDependencies_InvalidJsonType_ReturnsEmptyList()
    {
        // Arrange
        var json = @"""not an object""";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
    }

    [Fact]
    public void IsValidDependencyName_ValidNumericVersion_ReturnsTrue()
    {
        // Act
        var result = _service.IsValidDependencyName("Creator.Package.1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidDependencyName_ValidLatestVersion_ReturnsTrue()
    {
        // Act
        var result = _service.IsValidDependencyName("Creator.Package.latest");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidDependencyName_ValidWithoutVersion_ReturnsFalse()
    {
        // Dependencies should have versions according to spec
        // Act
        var result = _service.IsValidDependencyName("Creator.Package");

        // Assert
        result.Should().BeFalse(); // Requires version or "latest"
    }

    [Fact]
    public void IsValidDependencyName_Null_ReturnsFalse()
    {
        // Act
        var result = _service.IsValidDependencyName(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    public void IsValidDependencyName_EmptyOrWhitespace_ReturnsFalse(string name)
    {
        // Act
        var result = _service.IsValidDependencyName(name);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Creator.Package.Version.Extra")]
    [InlineData("Creator")]
    [InlineData("Creator.Package.Version.Extra.More")]
    public void IsValidDependencyName_InvalidFormat_ReturnsFalse(string name)
    {
        // Act
        var result = _service.IsValidDependencyName(name);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("Creator.Package.abc")]
    [InlineData("Creator.Package.v1")]
    [InlineData("Creator.Package.1.0")]
    public void IsValidDependencyName_InvalidVersion_ReturnsFalse(string name)
    {
        // Act
        var result = _service.IsValidDependencyName(name);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ExtractDependencies_ComplexNestedStructure_ExtractsAll()
    {
        // Arrange
        var json = @"{
            ""dependencies"": {
                ""Creator1.Package1.1"": {
                    ""licenseType"": ""MIT"",
                    ""dependencies"": {
                        ""Creator2.Package2.1"": {
                            ""optional"": true,
                            ""dependencies"": {
                                ""Creator3.Package3.latest"": {}
                            }
                        }
                    }
                },
                ""Creator4.Package4.latest"": {
                    ""missing"": true
                }
            }
        }";
        var jsonDoc = JsonDocument.Parse(json);

        // Act
        var result = _service.ExtractDependencies(jsonDoc.RootElement);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(4);
        result.Should().Contain(d => d.Name == "Creator1.Package1.1" && d.LicenseType == "MIT");
        result.Should().Contain(d => d.Name == "Creator2.Package2.1" && d.IsOptional);
        result.Should().Contain(d => d.Name == "Creator3.Package3.latest");
        result.Should().Contain(d => d.Name == "Creator4.Package4.latest" && d.IsMissing);
    }
}

