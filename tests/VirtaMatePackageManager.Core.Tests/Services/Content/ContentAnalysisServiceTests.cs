using FluentAssertions;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using VirtaMatePackageManager.Core.Services.Content;
using VirtaMatePackageManager.Core.ValueObjects.Content;
using Xunit;

namespace VirtaMatePackageManager.Core.Tests.Services.Content;

public class ContentAnalysisServiceTests : IDisposable
{
    private readonly ContentAnalysisService _service;
    private readonly string _testDirectory;

    public ContentAnalysisServiceTests()
    {
        _service = new ContentAnalysisService();
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    private string CreateTestVarFile(string varName, params (string path, string content)[] entries)
    {
        var filePath = Path.Combine(_testDirectory, varName);
        using (var zip = ZipFile.Open(filePath, ZipArchiveMode.Create))
        {
            foreach (var (path, content) in entries)
            {
                var entry = zip.CreateEntry(path);
                using (var writer = new StreamWriter(entry.Open()))
                {
                    writer.Write(content);
                }
            }
        }
        return filePath;
    }

    #region DetermineContentType Tests

    [Fact]
    public void DetermineContentType_ScenePath_ReturnsScene()
    {
        // Arrange
        var path = "saves/scene/MyScene.json";

        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Scene);
    }

    [Theory]
    [InlineData("saves/person/appearance/Look1.json")]
    [InlineData("saves/person/appearance/Look2.vac")]
    [InlineData("custom/atom/person/general/Look3.json")]
    [InlineData("custom/atom/person/appearance/Look4.vap")]
    public void DetermineContentType_LookPaths_ReturnsLook(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Look);
    }

    [Theory]
    [InlineData("custom/clothing/Shirt1.vam")]
    [InlineData("custom/clothing/Pants2.vap")]
    [InlineData("custom/atom/person/clothing/Dress3.vam")]
    [InlineData("custom/atom/person/clothing/Shoes4.vap")]
    public void DetermineContentType_ClothingPaths_ReturnsClothing(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Clothing);
    }

    [Theory]
    [InlineData("custom/hair/Hair1.vam")]
    [InlineData("custom/hair/Hair2.vap")]
    [InlineData("custom/atom/person/hair/Hair3.vam")]
    [InlineData("custom/atom/person/hair/Hair4.vap")]
    public void DetermineContentType_HairstylePaths_ReturnsHairstyle(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Hairstyle);
    }

    [Fact]
    public void DetermineContentType_ScriptPath_ReturnsScript()
    {
        // Arrange
        var path = "custom/scripts/MyScript.cs";

        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Script);
    }

    [Fact]
    public void DetermineContentType_ScriptListPath_ReturnsScriptList()
    {
        // Arrange
        var path = "custom/scripts/MyScriptList.cslist";

        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.ScriptList);
    }

    [Fact]
    public void DetermineContentType_AssetPath_ReturnsAsset()
    {
        // Arrange
        var path = "custom/assets/MyAsset.assetbundle";

        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Asset);
    }

    [Theory]
    [InlineData("custom/atom/person/morphs/Morph1.vmi")]
    [InlineData("custom/atom/person/morphs/Morph2.vap")]
    public void DetermineContentType_MorphPaths_ReturnsMorph(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Morph);
    }

    [Theory]
    [InlineData("saves/person/pose/Pose1.json")]
    [InlineData("saves/person/pose/Pose2.vac")]
    [InlineData("custom/atom/person/pose/Pose3.vap")]
    public void DetermineContentType_PosePaths_ReturnsPose(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Pose);
    }

    [Fact]
    public void DetermineContentType_SkinPath_ReturnsSkin()
    {
        // Arrange
        var path = "custom/atom/person/skin/Skin1.vap";

        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Skin);
    }

    [Theory]
    [InlineData("random/file.txt")]
    [InlineData("unknown/path/file.dat")]
    [InlineData("meta.json")]
    public void DetermineContentType_UnknownPath_ReturnsUnknown(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().Be(Core.Entities.ContentType.Unknown);
    }

    [Theory]
    [InlineData("SAVES/SCENE/SCENE.JSON")] // Uppercase
    [InlineData("Custom/Clothing/Shirt.VAM")] // Mixed case
    public void DetermineContentType_CaseInsensitive_Works(string path)
    {
        // Act
        var result = _service.DetermineContentType(path);

        // Assert
        result.Should().NotBe(Core.Entities.ContentType.Unknown);
    }

    #endregion

    #region DetermineIfPreset Tests

    [Fact]
    public void DetermineIfPreset_Scene_AlwaysReturnsTrue()
    {
        // Arrange
        var path = "saves/scene/AnyScene.json";
        var contentType = Core.Entities.ContentType.Scene;

        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("saves/person/appearance/Look1.json", Core.Entities.ContentType.Look, true)]
    [InlineData("saves/person/appearance/Look2.vap", Core.Entities.ContentType.Look, true)]
    [InlineData("saves/person/appearance/Look3.vac", Core.Entities.ContentType.Look, false)]
    public void DetermineIfPreset_Look_ChecksExtension(string path, Core.Entities.ContentType contentType, bool expected)
    {
        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("custom/clothing/Shirt.vap", Core.Entities.ContentType.Clothing, true)]
    [InlineData("custom/clothing/Pants.vam", Core.Entities.ContentType.Clothing, false)]
    public void DetermineIfPreset_Clothing_OnlyVapIsPreset(string path, Core.Entities.ContentType contentType, bool expected)
    {
        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("custom/hair/Hair.vap", Core.Entities.ContentType.Hairstyle, true)]
    [InlineData("custom/hair/Hair.vam", Core.Entities.ContentType.Hairstyle, false)]
    public void DetermineIfPreset_Hairstyle_OnlyVapIsPreset(string path, Core.Entities.ContentType contentType, bool expected)
    {
        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("custom/atom/person/morphs/Morph.vap", Core.Entities.ContentType.Morph, true)]
    [InlineData("custom/atom/person/morphs/Morph.vmi", Core.Entities.ContentType.Morph, false)]
    public void DetermineIfPreset_Morph_OnlyVapIsPreset(string path, Core.Entities.ContentType contentType, bool expected)
    {
        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("saves/person/pose/Pose.json", Core.Entities.ContentType.Pose, true)]
    [InlineData("saves/person/pose/Pose.vap", Core.Entities.ContentType.Pose, true)]
    [InlineData("saves/person/pose/Pose.vac", Core.Entities.ContentType.Pose, false)]
    public void DetermineIfPreset_Pose_JsonOrVapIsPreset(string path, Core.Entities.ContentType contentType, bool expected)
    {
        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void DetermineIfPreset_Skin_VapIsPreset()
    {
        // Arrange
        var path = "custom/atom/person/skin/Skin.vap";
        var contentType = Core.Entities.ContentType.Skin;

        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(Core.Entities.ContentType.Script)]
    [InlineData(Core.Entities.ContentType.ScriptList)]
    [InlineData(Core.Entities.ContentType.Asset)]
    [InlineData(Core.Entities.ContentType.Unknown)]
    public void DetermineIfPreset_NonPresetTypes_ReturnsFalse(Core.Entities.ContentType contentType)
    {
        // Arrange
        var path = "any/path/file.ext";

        // Act
        var result = _service.DetermineIfPreset(path, contentType);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region AnalyzeVarContent Tests

    [Fact]
    public void AnalyzeVarContent_EmptyVar_ReturnsEmptyResult()
    {
        // Arrange
        var varPath = CreateTestVarFile("Empty.var");

        // Act
        var result = _service.AnalyzeVarContent(varPath);

        // Assert
        result.Should().NotBeNull();
        result.ContentItems.Should().BeEmpty();
        result.ContentCounts.Scenes.Should().Be(0);
    }

    [Fact]
    public void AnalyzeVarContent_VarWithScene_ReturnsScene()
    {
        // Arrange
        var varPath = CreateTestVarFile("Scene.var",
            ("meta.json", "{}"),
            ("saves/scene/MyScene.json", "{}"));

        // Act
        var result = _service.AnalyzeVarContent(varPath);

        // Assert
        result.Should().NotBeNull();
        result.ContentItems.Should().HaveCount(1);
        result.ContentItems[0].ContentType.Should().Be(Core.Entities.ContentType.Scene);
        result.ContentItems[0].IsPreset.Should().BeTrue();
        result.ContentCounts.Scenes.Should().Be(1);
    }

    [Fact]
    public void AnalyzeVarContent_VarWithMultipleContentTypes_ReturnsAll()
    {
        // Arrange
        var varPath = CreateTestVarFile("Mixed.var",
            ("meta.json", "{}"),
            ("saves/scene/Scene1.json", "{}"),
            ("custom/clothing/Shirt.vap", "{}"),
            ("custom/hair/Hair.vap", "{}"),
            ("custom/scripts/Script.cs", "// script"));

        // Act
        var result = _service.AnalyzeVarContent(varPath);

        // Assert
        result.Should().NotBeNull();
        result.ContentItems.Should().HaveCount(4);
        result.ContentCounts.Scenes.Should().Be(1);
        result.ContentCounts.Clothing.Should().Be(1);
        result.ContentCounts.Hairstyles.Should().Be(1);
        result.ContentCounts.Scripts.Should().Be(1);
    }

    [Fact]
    public void AnalyzeVarContent_SkipsDirectories()
    {
        // Arrange
        var varPath = CreateTestVarFile("WithDirs.var",
            ("meta.json", "{}"),
            ("saves/scene/", ""), // Directory entry
            ("saves/scene/Scene.json", "{}"));

        // Act
        var result = _service.AnalyzeVarContent(varPath);

        // Assert
        result.Should().NotBeNull();
        result.ContentItems.Should().HaveCount(1); // Directory skipped
        result.ContentItems[0].ContentType.Should().Be(Core.Entities.ContentType.Scene);
    }

    [Fact]
    public void AnalyzeVarContent_UnknownFiles_CountedAsUnknown()
    {
        // Arrange
        var varPath = CreateTestVarFile("WithUnknown.var",
            ("meta.json", "{}"),
            ("random/file.txt", "text"),
            ("unknown/data.dat", "data"));

        // Act
        var result = _service.AnalyzeVarContent(varPath);

        // Assert
        result.Should().NotBeNull();
        result.ContentItems.Should().BeEmpty(); // Unknown items not added to ContentItems
        // meta.json, random/file.txt, and unknown/data.dat are all unknown (3 files)
        result.ContentCounts.Unknown.Should().Be(3);
    }

    [Fact]
    public void AnalyzeVarContent_NonExistentFile_ThrowsInvalidOperationException()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "NonExistent.var");

        // Act
        Action act = () => _service.AnalyzeVarContent(nonExistentPath);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AnalyzeVarContent_InvalidZipFile_ThrowsInvalidOperationException()
    {
        // Arrange
        var invalidPath = Path.Combine(_testDirectory, "Invalid.var");
        File.WriteAllText(invalidPath, "Not a ZIP file");

        // Act
        Action act = () => _service.AnalyzeVarContent(invalidPath);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AnalyzeVarContent_PresetsCorrectlyIdentified()
    {
        // Arrange
        var varPath = CreateTestVarFile("Presets.var",
            ("meta.json", "{}"),
            ("saves/person/appearance/Look.json", "{}"), // Preset (JSON)
            ("custom/clothing/Shirt.vap", "{}"), // Preset (VAP)
            ("custom/clothing/Pants.vam", "{}"), // Not preset (VAM)
            ("saves/scene/Scene.json", "{}")); // Preset (always)

        // Act
        var result = _service.AnalyzeVarContent(varPath);

        // Assert
        result.Should().NotBeNull();
        var lookItem = result.ContentItems.FirstOrDefault(i => i.ContentType == Core.Entities.ContentType.Look);
        lookItem.Should().NotBeNull();
        lookItem!.IsPreset.Should().BeTrue();

        var clothingVap = result.ContentItems.FirstOrDefault(i => i.Path.Contains("Shirt"));
        clothingVap.Should().NotBeNull();
        clothingVap!.IsPreset.Should().BeTrue();

        var clothingVam = result.ContentItems.FirstOrDefault(i => i.Path.Contains("Pants"));
        clothingVam.Should().NotBeNull();
        clothingVam!.IsPreset.Should().BeFalse();

        var scene = result.ContentItems.FirstOrDefault(i => i.ContentType == Core.Entities.ContentType.Scene);
        scene.Should().NotBeNull();
        scene!.IsPreset.Should().BeTrue();
    }

    #endregion
}

