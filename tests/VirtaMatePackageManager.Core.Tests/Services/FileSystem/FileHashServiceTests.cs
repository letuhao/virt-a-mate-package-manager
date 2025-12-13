using FluentAssertions;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VirtaMatePackageManager.Core.Services.FileSystem;
using Xunit;

namespace VirtaMatePackageManager.Core.Tests.Services.FileSystem;

public class FileHashServiceTests : IDisposable
{
    private readonly FileHashService _service;
    private readonly string _testDirectory;

    public FileHashServiceTests()
    {
        _service = new FileHashService();
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

    private string CreateTestFile(string fileName, string content)
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        File.WriteAllText(filePath, content);
        return filePath;
    }

    private string ComputeExpectedHash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    [Fact]
    public void ComputeFileHash_ValidFile_ReturnsCorrectHash()
    {
        // Arrange
        var content = "Test file content for hashing";
        var filePath = CreateTestFile("test.txt", content);
        var expectedHash = ComputeExpectedHash(content);

        // Act
        var result = _service.ComputeFileHash(filePath);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(expectedHash);
        result.Should().HaveLength(64); // SHA-256 produces 64 hex characters
    }

    [Fact]
    public void ComputeFileHash_EmptyFile_ReturnsHash()
    {
        // Arrange
        var filePath = CreateTestFile("empty.txt", string.Empty);
        var expectedHash = ComputeExpectedHash(string.Empty);

        // Act
        var result = _service.ComputeFileHash(filePath);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(expectedHash);
    }

    [Fact]
    public void ComputeFileHash_LargeFile_ReturnsHash()
    {
        // Arrange
        var largeContent = new string('A', 10000); // 10KB of data
        var filePath = CreateTestFile("large.txt", largeContent);
        var expectedHash = ComputeExpectedHash(largeContent);

        // Act
        var result = _service.ComputeFileHash(filePath);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(expectedHash);
    }

    [Fact]
    public void ComputeFileHash_FileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        Action act = () => _service.ComputeFileHash(nonExistentFile);

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ComputeFileHash_NullPath_ThrowsArgumentNullException()
    {
        // Act
        Action act = () => _service.ComputeFileHash(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ComputeFileHash_EmptyPath_ThrowsArgumentException()
    {
        // Act
        Action act = () => _service.ComputeFileHash(string.Empty);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ComputeFileHashAsync_ValidFile_ReturnsCorrectHash()
    {
        // Arrange
        var content = "Test file content for async hashing";
        var filePath = CreateTestFile("test_async.txt", content);
        var expectedHash = ComputeExpectedHash(content);

        // Act
        var result = await _service.ComputeFileHashAsync(filePath, CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(expectedHash);
        result.Should().HaveLength(64);
    }

    [Fact]
    public async Task ComputeFileHashAsync_EmptyFile_ReturnsHash()
    {
        // Arrange
        var filePath = CreateTestFile("empty_async.txt", string.Empty);
        var expectedHash = ComputeExpectedHash(string.Empty);

        // Act
        var result = await _service.ComputeFileHashAsync(filePath, CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(expectedHash);
    }

    [Fact]
    public async Task ComputeFileHashAsync_LargeFile_ReturnsHash()
    {
        // Arrange
        var largeContent = new string('B', 50000); // 50KB of data
        var filePath = CreateTestFile("large_async.txt", largeContent);
        var expectedHash = ComputeExpectedHash(largeContent);

        // Act
        var result = await _service.ComputeFileHashAsync(filePath, CancellationToken.None);

        // Assert
        result.Should().NotBeNullOrEmpty();
        result.Should().Be(expectedHash);
    }

    [Fact]
    public async Task ComputeFileHashAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent_async.txt");

        // Act
        Func<Task> act = async () => await _service.ComputeFileHashAsync(nonExistentFile, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ComputeFileHashAsync_NullPath_ThrowsArgumentNullException()
    {
        // Act
        Func<Task> act = async () => await _service.ComputeFileHashAsync(null!, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ComputeFileHashAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var content = "Test content";
        var filePath = CreateTestFile("cancelled.txt", content);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _service.ComputeFileHashAsync(filePath, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ComputeFileHash_SameContentDifferentFiles_ReturnsSameHash()
    {
        // Arrange
        var content = "Identical content";
        var file1 = CreateTestFile("file1.txt", content);
        var file2 = CreateTestFile("file2.txt", content);

        // Act
        var hash1 = _service.ComputeFileHash(file1);
        var hash2 = _service.ComputeFileHash(file2);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeFileHash_DifferentContent_ReturnsDifferentHash()
    {
        // Arrange
        var file1 = CreateTestFile("file1.txt", "Content 1");
        var file2 = CreateTestFile("file2.txt", "Content 2");

        // Act
        var hash1 = _service.ComputeFileHash(file1);
        var hash2 = _service.ComputeFileHash(file2);

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public async Task ComputeFileHashAsync_SameContentDifferentFiles_ReturnsSameHash()
    {
        // Arrange
        var content = "Identical async content";
        var file1 = CreateTestFile("async_file1.txt", content);
        var file2 = CreateTestFile("async_file2.txt", content);

        // Act
        var hash1 = await _service.ComputeFileHashAsync(file1, CancellationToken.None);
        var hash2 = await _service.ComputeFileHashAsync(file2, CancellationToken.None);

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public async Task ComputeFileHash_SyncAndAsync_ReturnSameHash()
    {
        // Arrange
        var content = "Same content for sync and async";
        var filePath = CreateTestFile("sync_async.txt", content);

        // Act
        var syncHash = _service.ComputeFileHash(filePath);
        var asyncHash = await _service.ComputeFileHashAsync(filePath, CancellationToken.None);

        // Assert
        syncHash.Should().Be(asyncHash);
    }

    [Fact]
    public void FilesAreIdentical_SameContent_ReturnsTrue()
    {
        // Arrange
        var content = "Identical content for comparison";
        var file1 = CreateTestFile("compare1.txt", content);
        var file2 = CreateTestFile("compare2.txt", content);

        // Act
        var result = _service.FilesAreIdentical(file1, file2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void FilesAreIdentical_DifferentContent_ReturnsFalse()
    {
        // Arrange
        var file1 = CreateTestFile("compare1.txt", "Content 1");
        var file2 = CreateTestFile("compare2.txt", "Content 2");

        // Act
        var result = _service.FilesAreIdentical(file1, file2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void FilesAreIdentical_DifferentSizes_ReturnsFalse()
    {
        // Arrange
        var file1 = CreateTestFile("compare1.txt", "Short");
        var file2 = CreateTestFile("compare2.txt", "Much longer content here");

        // Act
        var result = _service.FilesAreIdentical(file1, file2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void FilesAreIdentical_OneFileMissing_ReturnsFalse()
    {
        // Arrange
        var file1 = CreateTestFile("exists.txt", "Content");
        var file2 = Path.Combine(_testDirectory, "missing.txt");

        // Act
        var result = _service.FilesAreIdentical(file1, file2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task FilesAreIdenticalAsync_SameContent_ReturnsTrue()
    {
        // Arrange
        var content = "Identical async content for comparison";
        var file1 = CreateTestFile("async_compare1.txt", content);
        var file2 = CreateTestFile("async_compare2.txt", content);

        // Act
        var result = await _service.FilesAreIdenticalAsync(file1, file2, CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task FilesAreIdenticalAsync_DifferentContent_ReturnsFalse()
    {
        // Arrange
        var file1 = CreateTestFile("async_compare1.txt", "Content 1");
        var file2 = CreateTestFile("async_compare2.txt", "Content 2");

        // Act
        var result = await _service.FilesAreIdenticalAsync(file1, file2, CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task FilesAreIdenticalAsync_Cancelled_ThrowsOperationCanceledException()
    {
        // Arrange
        var content = "Test content";
        var file1 = CreateTestFile("cancel1.txt", content);
        var file2 = CreateTestFile("cancel2.txt", content);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        Func<Task> act = async () => await _service.FilesAreIdenticalAsync(file1, file2, cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

