using Namerre.Application.Folders;
using NSubstitute;
using Xunit;

namespace Namerre.Application.UnitTests.Folders;

public sealed class GetFolderQueryHandlerTests
{
    [Fact]
    public async Task ForwardsPathAndCancellationAndReturnsListing()
    {
        var browser = Substitute.For<IFolderBrowser>();
        using var cancellation = new CancellationTokenSource();
        var listing = new FolderListing("Movies", "", [], []);
        browser.Browse("Movies", cancellation.Token).Returns(Task.FromResult(listing));
        var handler = new GetFolderQueryHandler(browser);

        var result = await handler.Handle(new GetFolderQuery("Movies"), cancellation.Token);

        Assert.Same(listing, result);
        await browser.Received(1).Browse("Movies", cancellation.Token);
    }
}
