using System.Security.Cryptography;
using System.Text;
using Namerre.Application.Renaming;
using Namerre.Infrastructure.Renaming;
using Xunit;

namespace Namerre.Infrastructure.UnitTests.Renaming;

public sealed class BoundedFileSystemTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "namerre-infra-tests", Guid.NewGuid().ToString("N"));
    private readonly string root;
    private readonly BoundedFileSystem fileSystem;

    public BoundedFileSystemTests()
    {
        root = Path.Combine(workspace, "media");
        Directory.CreateDirectory(Path.Combine(root, "Movies"));
        File.WriteAllText(Path.Combine(root, "Movies", "film.mkv"), "content");
        fileSystem = new BoundedFileSystem("media", workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void UnconfiguredRootIsUnavailable(string? configured)
    {
        var exception = Assert.Throws<RenameException>(() => _ = new BoundedFileSystem(configured, workspace).Root);

        Assert.Equal("RootUnavailable", exception.Code);
        Assert.Equal(503, exception.Status);
    }

    [Fact]
    public void MissingRootIsUnavailable()
        => Assert.Equal("RootUnavailable", Assert.Throws<RenameException>(() =>
            _ = new BoundedFileSystem("missing", workspace).Root).Code);

    [Fact]
    public void RootIsResolvedAgainstTheBasePathWithoutTrailingSeparator()
    {
        Assert.Equal(root, fileSystem.Root);
        Assert.Equal(root, new BoundedFileSystem(root + Path.DirectorySeparatorChar, workspace).Root);
    }

    [Fact]
    public void RootThatIsAFileIsUnavailable()
    {
        File.WriteAllText(Path.Combine(workspace, "file-root"), "");

        Assert.Equal("RootUnavailable", Assert.Throws<RenameException>(() =>
            _ = new BoundedFileSystem("file-root", workspace).Root).Code);
    }

    [Fact]
    public void ResolveMapsRelativePathsUnderTheRoot()
        => Assert.Equal(Path.Combine(root, "Movies", "film.mkv"), fileSystem.Resolve("Movies/film.mkv"));

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("Movies\\film.mkv")]
    [InlineData("C:/Windows")]
    [InlineData("Movies/.namerre-stage-0123")]
    [InlineData("")]
    public void ResolveRejectsPathsOutsideOrReserved(string path)
    {
        var exception = Assert.Throws<RenameException>(() => fileSystem.Resolve(path));

        Assert.Equal("InvalidPath", exception.Code);
        Assert.Equal(400, exception.Status);
    }

    [Fact]
    public void InternalStagingNamesRequireAGuidSuffix()
    {
        var staging = ".namerre-stage-" + Guid.NewGuid().ToString("N");

        Assert.Equal(Path.Combine(root, "Movies", staging), fileSystem.Resolve("Movies/" + staging, allowInternal: true));
        Assert.Equal(Path.Combine(root, staging), fileSystem.Resolve(staging, allowInternal: true));
        foreach (var invalid in new[] { "Movies/.namerre-stage-nope", "Movies/film.mkv", "../" + staging })
            Assert.Equal("InvalidPath", Assert.Throws<RenameException>(() =>
                fileSystem.Resolve(invalid, allowInternal: true)).Code);
    }

    [Fact]
    public void ResolveRejectsPathsBelowAFile()
        => Assert.Equal("InvalidParent", Assert.Throws<RenameException>(() =>
            fileSystem.Resolve("Movies/film.mkv/child.txt")).Code);

    [Fact]
    public void ExistsDetectsFilesDirectoriesAndStagingEntries()
    {
        var staging = ".namerre-stage-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(root, staging), "");

        Assert.True(fileSystem.Exists("Movies/film.mkv"));
        Assert.True(fileSystem.Exists("Movies"));
        Assert.True(fileSystem.Exists(staging));
        Assert.False(fileSystem.Exists("Movies/other.mkv"));
        Assert.Equal("InvalidPath", Assert.Throws<RenameException>(() => fileSystem.Exists("../media")).Code);
    }

    [Fact]
    public void ValidateAcceptsMissingButWellFormedPaths()
    {
        fileSystem.Validate("New/Folder/file.txt");

        Assert.Throws<RenameException>(() => fileSystem.Validate("New/../file.txt"));
    }

    [Fact]
    public void SamePathShortCircuitsIdenticalPaths()
        => Assert.True(fileSystem.SamePath("Movies/film.mkv", "Movies/film.mkv"));

    [Fact]
    public async Task HashReturnsSha256AndSize()
    {
        var (hash, size) = await fileSystem.Hash("Movies/film.mkv", default);

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("content"))), hash);
        Assert.Equal(7, size);
    }

    [Theory]
    [InlineData("Movies/missing.mkv")]
    [InlineData("Movies")]
    public async Task HashRequiresAnExistingFile(string path)
        => Assert.Equal("SourceMissing", (await Assert.ThrowsAsync<RenameException>(() =>
            fileSystem.Hash(path, default))).Code);

    [Fact]
    public async Task HashHonoursCancellation()
        => await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fileSystem.Hash("Movies/film.mkv", new CancellationToken(true)));

    [Fact]
    public void MissingDirectoriesAreListedOutermostFirst()
    {
        Assert.Equal(["Movies/2024", "Movies/2024/Alien"], fileSystem.MissingDirectories("Movies/2024/Alien/alien.mkv"));
        Assert.Empty(fileSystem.MissingDirectories("Movies/renamed.mkv"));
        Assert.Equal(["New"], fileSystem.MissingDirectories("New/file.mkv"));
    }

    [Fact]
    public void DeleteEmptyDirectoryKeepsNonEmptyAndMissingDirectories()
    {
        Directory.CreateDirectory(Path.Combine(root, "Empty"));

        fileSystem.DeleteEmptyDirectory("Empty");
        fileSystem.DeleteEmptyDirectory("Movies");
        fileSystem.DeleteEmptyDirectory("Missing");

        Assert.False(Directory.Exists(Path.Combine(root, "Empty")));
        Assert.True(File.Exists(Path.Combine(root, "Movies", "film.mkv")));
    }

    [Fact]
    public void CreateDirectoryRequiresAnExistingParentAndAnUnusedName()
    {
        Assert.Equal("ParentMissing", Assert.Throws<RenameException>(() =>
            fileSystem.CreateDirectory("Missing/Child")).Code);
        Assert.Equal("DirectoryChanged", Assert.Throws<RenameException>(() =>
            fileSystem.CreateDirectory("Movies")).Code);
        Assert.Equal("DirectoryChanged", Assert.Throws<RenameException>(() =>
            fileSystem.CreateDirectory("Movies/film.mkv")).Code);
    }

    [Fact]
    public void ActualPathRequiresEveryComponentToExist()
        => Assert.Equal("SourceMissing", Assert.Throws<RenameException>(() =>
            fileSystem.ActualPath("Movies/missing.mkv")).Code);

    [Fact]
    public void ActualPathReturnsStoredNames()
        => Assert.Equal("Movies/film.mkv", fileSystem.ActualPath("Movies/film.mkv"));

    [Fact]
    public void CheckAncestorsAcceptsMissingLeavesAndRejectsFileParents()
    {
        BoundedFileSystem.CheckAncestors(Path.Combine(root, "Movies", "not-yet.mkv"));
        BoundedFileSystem.CheckAncestors(Path.Combine(root, "Movies", "film.mkv"));

        Assert.Equal("InvalidParent", Assert.Throws<RenameException>(() =>
            BoundedFileSystem.CheckAncestors(Path.Combine(root, "Movies", "film.mkv", "x"))).Code);
    }
}
