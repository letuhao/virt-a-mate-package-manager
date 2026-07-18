using VarVault.Common;
using VarVault.Domain.ValueObjects;

namespace VarVault.Common.Tests;

public class ResultTests
{
    [Fact]
    public void Success_carries_value()
    {
        var r = Result.Success(42);
        Assert.True(r.IsSuccess);
        Assert.Equal(42, r.Value);
    }

    [Fact]
    public void Failure_exposes_error_and_throws_on_value()
    {
        Result<int> r = new Error("x", "boom");
        Assert.True(r.IsFailure);
        Assert.Equal("x", r.Error.Code);
        Assert.Throws<InvalidOperationException>(() => r.Value);
    }

    [Fact]
    public void Map_only_runs_on_success()
    {
        Assert.Equal(4, Result.Success(2).Map(x => x * 2).Value);
        Assert.True(Result<int>.Failure(new Error("e", "e")).Map(x => x * 2).IsFailure);
    }
}

public class GuardTests
{
    [Fact]
    public void NotNullOrWhiteSpace_rejects_blank() =>
        Assert.Throws<ArgumentException>(() => Guard.NotNullOrWhiteSpace("  "));

    [Fact]
    public void NotNull_returns_value() =>
        Assert.Equal("ok", Guard.NotNull("ok"));
}

public class PackageIdTests
{
    [Theory]
    [InlineData("Creator.Package.7", "Creator", "Package", "7")]
    [InlineData("Creator.Package.007.var", "Creator", "Package", "007")] // leading zeros preserved
    public void Parses_valid_names(string input, string creator, string pkg, string ver)
    {
        var r = PackageId.TryParse(input);
        Assert.True(r.IsSuccess);
        Assert.Equal(creator, r.Value.Creator);
        Assert.Equal(pkg, r.Value.Package);
        Assert.Equal(ver, r.Value.VersionToken);
    }

    [Theory]
    [InlineData("A.B.C.1")]        // 4 parts
    [InlineData("A.B.latest")]     // non-numeric version
    [InlineData("NoDots")]
    public void Rejects_invalid_names(string input) =>
        Assert.True(PackageId.TryParse(input).IsFailure);
}
