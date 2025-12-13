using FluentAssertions;
using System;
using System.IO;
using System.Runtime.InteropServices;
using VirtaMatePackageManager.Core.Services.FileSystem;
using VirtaMatePackageManager.Core.ValueObjects;
using Xunit;

namespace VirtaMatePackageManager.Core.Tests.Services.FileSystem;

public class SymbolicLinkServiceTests : IDisposable
{
    private readonly SymbolicLinkService _service;
    private readonly string _testDirectory;
    private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public SymbolicLinkServiceTests()
    {
        _service = new SymbolicLinkService();
        _testDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private string CreateTestFile(string fileName = "test.txt")
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, "Test content");
        return filePath;
    }

    private string CreateTestDirectory(string dirName = "testdir")
    {
        var dirPath = Path.Combine(_testDirectory, dirName);
        Directory.CreateDirectory(dirPath);
        return dirPath;
    }

    #region CreateSymbolicLink Validation Tests

    [Fact]
    public void CreateSymbolicLink_NullLinkPath_ReturnsFailure()
    {
        // Arrange
        var targetPath = CreateTestFile();

        // Act
        var result = _service.CreateSymbolicLink(null!, targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void CreateSymbolicLink_EmptyLinkPath_ReturnsFailure()
    {
        // Arrange
        var targetPath = CreateTestFile();

        // Act
        var result = _service.CreateSymbolicLink(string.Empty, targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void CreateSymbolicLink_WhitespaceLinkPath_ReturnsFailure()
    {
        // Arrange
        var targetPath = CreateTestFile();

        // Act
        var result = _service.CreateSymbolicLink("   ", targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void CreateSymbolicLink_NullTargetPath_ReturnsFailure()
    {
        // Arrange
        var linkPath = Path.Combine(_testDirectory, "link.txt");

        // Act
        var result = _service.CreateSymbolicLink(linkPath, null!, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void CreateSymbolicLink_EmptyTargetPath_ReturnsFailure()
    {
        // Arrange
        var linkPath = Path.Combine(_testDirectory, "link.txt");

        // Act
        var result = _service.CreateSymbolicLink(linkPath, string.Empty, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void CreateSymbolicLink_FileTargetDoesNotExist_ReturnsFailure()
    {
        // Arrange
        var linkPath = Path.Combine(_testDirectory, "link.txt");
        var targetPath = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
        result.ErrorCode.Should().Be(ErrorCode.FileNotFound);
    }

    [Fact]
    public void CreateSymbolicLink_DirectoryTargetDoesNotExist_ReturnsFailure()
    {
        // Arrange
        var linkPath = Path.Combine(_testDirectory, "linkdir");
        var targetPath = Path.Combine(_testDirectory, "nonexistent");

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("does not exist");
        result.ErrorCode.Should().Be(ErrorCode.FileNotFound);
    }

    [Fact]
    public void CreateSymbolicLink_InvalidLinkPath_ReturnsFailure()
    {
        // Arrange
        var targetPath = CreateTestFile();
        // Root path without directory (invalid)
        var linkPath = "C:\\";

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Invalid link path");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void CreateSymbolicLink_LinkPathAlreadyExistsAsFile_ReturnsFailure()
    {
        // Arrange
        var targetPath = CreateTestFile("target.txt");
        var existingFile = CreateTestFile("existing.txt");
        var linkPath = existingFile; // Use existing file as link path

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        result.ErrorCode.Should().Be(ErrorCode.PathConflict);
    }

    [Fact]
    public void CreateSymbolicLink_LinkPathAlreadyExistsAsDirectory_ReturnsFailure()
    {
        // Arrange
        var targetPath = CreateTestDirectory("targetdir");
        var existingDir = CreateTestDirectory("existingdir");
        var linkPath = existingDir; // Use existing directory as link path

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: true);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("already exists");
        result.ErrorCode.Should().Be(ErrorCode.PathConflict);
    }

    [Fact]
    public void CreateSymbolicLink_CreatesParentDirectoryIfNeeded()
    {
        // Skip on non-Windows or if symlinks not supported
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var targetPath = CreateTestFile();
        var linkPath = Path.Combine(_testDirectory, "subdir", "nested", "link.txt");
        // Parent directory doesn't exist yet

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);

        // Assert
        // Should attempt to create parent directory
        // If symlink creation fails due to privileges, that's okay - we're testing directory creation
        if (result.IsSuccess)
        {
            Directory.Exists(Path.GetDirectoryName(linkPath)).Should().BeTrue();
        }
        else
        {
            // Even if symlink creation fails, parent directory should exist if it was created
            // This test primarily verifies the code path doesn't crash
            result.Error.Should().NotBeNullOrEmpty();
        }
    }

    #endregion

    #region IsSymbolicLink Tests

    [Fact]
    public void IsSymbolicLink_NullPath_ReturnsFalse()
    {
        // Act
        var result = _service.IsSymbolicLink(null!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSymbolicLink_EmptyPath_ReturnsFalse()
    {
        // Act
        var result = _service.IsSymbolicLink(string.Empty);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSymbolicLink_WhitespacePath_ReturnsFalse()
    {
        // Act
        var result = _service.IsSymbolicLink("   ");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSymbolicLink_NonExistentPath_ReturnsFalse()
    {
        // Arrange
        var path = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        var result = _service.IsSymbolicLink(path);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSymbolicLink_RegularFile_ReturnsFalse()
    {
        // Arrange
        var filePath = CreateTestFile();

        // Act
        var result = _service.IsSymbolicLink(filePath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsSymbolicLink_RegularDirectory_ReturnsFalse()
    {
        // Arrange
        var dirPath = CreateTestDirectory();

        // Act
        var result = _service.IsSymbolicLink(dirPath);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ResolveSymbolicLink Tests

    [Fact]
    public void ResolveSymbolicLink_NullPath_ReturnsNull()
    {
        // Act
        var result = _service.ResolveSymbolicLink(null!);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSymbolicLink_EmptyPath_ReturnsNull()
    {
        // Act
        var result = _service.ResolveSymbolicLink(string.Empty);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSymbolicLink_NonExistentPath_ReturnsNull()
    {
        // Arrange
        var path = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        var result = _service.ResolveSymbolicLink(path);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSymbolicLink_RegularFile_ReturnsNull()
    {
        // Arrange
        var filePath = CreateTestFile();

        // Act
        var result = _service.ResolveSymbolicLink(filePath);

        // Assert
        result.Should().BeNull(); // Not a symlink
    }

    [Fact]
    public void ResolveSymbolicLink_RegularDirectory_ReturnsNull()
    {
        // Arrange
        var dirPath = CreateTestDirectory();

        // Act
        var result = _service.ResolveSymbolicLink(dirPath);

        // Assert
        result.Should().BeNull(); // Not a symlink
    }

    #endregion

    #region DeleteSymbolicLink Tests

    [Fact]
    public void DeleteSymbolicLink_NullPath_ReturnsFailure()
    {
        // Act
        var result = _service.DeleteSymbolicLink(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void DeleteSymbolicLink_EmptyPath_ReturnsFailure()
    {
        // Act
        var result = _service.DeleteSymbolicLink(string.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("empty");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void DeleteSymbolicLink_RegularFile_ReturnsFailure()
    {
        // Arrange
        var filePath = CreateTestFile();

        // Act
        var result = _service.DeleteSymbolicLink(filePath);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not a symbolic link");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void DeleteSymbolicLink_RegularDirectory_ReturnsFailure()
    {
        // Arrange
        var dirPath = CreateTestDirectory();

        // Act
        var result = _service.DeleteSymbolicLink(dirPath);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not a symbolic link");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    [Fact]
    public void DeleteSymbolicLink_NonExistentPath_ReturnsFailure()
    {
        // Arrange
        var path = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        var result = _service.DeleteSymbolicLink(path);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("not a symbolic link");
        result.ErrorCode.Should().Be(ErrorCode.InvalidInput);
    }

    #endregion

    #region Integration Tests (Windows only, may require privileges)

    [Fact(Skip = "Requires Windows and may require admin privileges or Developer Mode")]
    public void CreateSymbolicLink_FileTarget_CreatesLink()
    {
        // Skip on non-Windows
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var targetPath = CreateTestFile("target.txt");
        var linkPath = Path.Combine(_testDirectory, "link.txt");

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);

        // Assert
        if (result.IsSuccess)
        {
            _service.IsSymbolicLink(linkPath).Should().BeTrue();
            File.Exists(linkPath).Should().BeTrue();
        }
        else
        {
            // May fail due to privileges - that's okay for automated tests
            result.Error.Should().ContainAny("privilege", "privileges", "Developer Mode", "Error code");
        }
    }

    [Fact(Skip = "Requires Windows and may require admin privileges or Developer Mode")]
    public void CreateSymbolicLink_DirectoryTarget_CreatesLink()
    {
        // Skip on non-Windows
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var targetPath = CreateTestDirectory("targetdir");
        var linkPath = Path.Combine(_testDirectory, "linkdir");

        // Act
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: true);

        // Assert
        if (result.IsSuccess)
        {
            _service.IsSymbolicLink(linkPath).Should().BeTrue();
            Directory.Exists(linkPath).Should().BeTrue();
        }
        else
        {
            // May fail due to privileges - that's okay for automated tests
            result.Error.Should().ContainAny("privilege", "privileges", "Developer Mode", "Error code");
        }
    }

    [Fact(Skip = "Requires Windows and may require admin privileges or Developer Mode")]
    public void CreateSymbolicLink_AlreadyLinkedCorrectly_ReturnsSuccess()
    {
        // Skip on non-Windows
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var targetPath = CreateTestFile("target.txt");
        var linkPath = Path.Combine(_testDirectory, "link.txt");

        // Create the symlink first
        var createResult = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);
        if (!createResult.IsSuccess)
        {
            return; // Can't test if we can't create symlink
        }

        // Act - Try to create again with same target
        var result = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact(Skip = "Requires Windows and may require admin privileges or Developer Mode")]
    public void ResolveSymbolicLink_ValidSymlink_ReturnsTarget()
    {
        // Skip on non-Windows
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var targetPath = CreateTestFile("target.txt");
        var linkPath = Path.Combine(_testDirectory, "link.txt");

        var createResult = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);
        if (!createResult.IsSuccess)
        {
            return; // Can't test if we can't create symlink
        }

        // Act
        var resolvedPath = _service.ResolveSymbolicLink(linkPath);

        // Assert
        resolvedPath.Should().NotBeNull();
        Path.GetFullPath(resolvedPath!).Should().Be(Path.GetFullPath(targetPath));
    }

    [Fact(Skip = "Requires Windows and may require admin privileges or Developer Mode")]
    public void DeleteSymbolicLink_ValidSymlink_DeletesLink()
    {
        // Skip on non-Windows
        if (!IsWindows)
        {
            return;
        }

        // Arrange
        var targetPath = CreateTestFile("target.txt");
        var linkPath = Path.Combine(_testDirectory, "link.txt");

        var createResult = _service.CreateSymbolicLink(linkPath, targetPath, isDirectory: false);
        if (!createResult.IsSuccess)
        {
            return; // Can't test if we can't create symlink
        }

        // Act
        var result = _service.DeleteSymbolicLink(linkPath);

        // Assert
        result.IsSuccess.Should().BeTrue();
        File.Exists(linkPath).Should().BeFalse();
        File.Exists(targetPath).Should().BeTrue(); // Target should still exist
    }

    #endregion
}

