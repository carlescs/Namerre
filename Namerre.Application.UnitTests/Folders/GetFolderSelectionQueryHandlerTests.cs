using Namerre.Application.Folders;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Folders;

public sealed class GetFolderSelectionQueryHandlerTests
{
    [Fact]
    public async Task WithoutSubfoldersOnlyBrowsesSelectedFolder()
    {
        var browser = Substitute.For<IFolderBrowser>();
        using var cancellation = new CancellationTokenSource();
        var folder = new FolderListing("Movies", "", [new FolderEntry("Nested", "Movies/Nested")], []);
        browser.Browse("Movies", cancellation.Token).Returns(Task.FromResult(folder));
        var handler = new GetFolderSelectionQueryHandler(browser);

        var result = await handler.Handle(new GetFolderSelectionQuery("Movies", false), cancellation.Token);

        Assert.Equal([folder], result);
        await browser.Received(1).Browse("Movies", cancellation.Token);
        await browser.DidNotReceive().Browse("Movies/Nested", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IncludesEveryDescendantAndPreservesFolderPaths()
    {
        var browser = Substitute.For<IFolderBrowser>();
        using var cancellation = new CancellationTokenSource();
        var root = new FolderListing("", null,
            [new FolderEntry("Empty", "Empty"), new FolderEntry("Movies", "Movies")], []);
        var empty = new FolderListing("Empty", "", [], []);
        var movies = new FolderListing("Movies", "", [new FolderEntry("Nested", "Movies/Nested")], []);
        var nested = new FolderListing("Movies/Nested", "Movies", [], []);
        foreach (var folder in new[] { root, empty, movies, nested })
        {
            browser.Browse(folder.Path, cancellation.Token).Returns(Task.FromResult(folder));
        }
        var handler = new GetFolderSelectionQueryHandler(browser);

        var result = await handler.Handle(new GetFolderSelectionQuery("", true), cancellation.Token);

        Assert.Equal([root, empty, movies, nested], result);
        foreach (var folder in result)
        {
            await browser.Received(1).Browse(folder.Path, cancellation.Token);
        }
    }

    [Fact]
    public async Task PropagatesSubfolderErrorsWithoutReturningPartialResults()
    {
        var browser = Substitute.For<IFolderBrowser>();
        var root = new FolderListing("", null, [new FolderEntry("Movies", "Movies")], []);
        var error = new FolderBrowserException(FolderBrowserError.Forbidden, "Cannot browse.");
        browser.Browse("", Arg.Any<CancellationToken>()).Returns(Task.FromResult(root));
        browser.Browse("Movies", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FolderListing>(error));
        var handler = new GetFolderSelectionQueryHandler(browser);

        var thrown = await Assert.ThrowsAsync<FolderBrowserException>(
            () => handler.Handle(new GetFolderSelectionQuery("", true), CancellationToken.None));

        Assert.Same(error, thrown);
    }

    [Fact]
    public async Task HonorsCancellationBeforeBrowsing()
    {
        var browser = Substitute.For<IFolderBrowser>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = new GetFolderSelectionQueryHandler(browser);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new GetFolderSelectionQuery("", true), cancellation.Token));

        await browser.DidNotReceive().Browse(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HonorsCancellationBetweenFolders()
    {
        var browser = Substitute.For<IFolderBrowser>();
        using var cancellation = new CancellationTokenSource();
        var root = new FolderListing("", null, [new FolderEntry("Movies", "Movies")], []);
        browser.Browse("", cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromResult(root);
        });
        var handler = new GetFolderSelectionQueryHandler(browser);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => handler.Handle(new GetFolderSelectionQuery("", true), cancellation.Token));

        await browser.DidNotReceive().Browse("Movies", Arg.Any<CancellationToken>());
    }
}
