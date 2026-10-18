using CMediator.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Folders;
using Namerre.Controllers;
using NSubstitute;
using Xunit;

namespace Namerre.Api.UnitTests.Controllers;

public sealed class FoldersControllerTests
{
    [Fact]
    public async Task SendsSelectedPathAndCancellationToken()
    {
        var sender = Substitute.For<ISender>();
        var listing = new FolderListing("Movies", "", [], []);
        using var cancellation = new CancellationTokenSource();
        sender.Send(Arg.Any<GetFolderQuery>(), cancellation.Token).Returns(Task.FromResult(listing));
        var controller = new FoldersController(sender, NullLogger<FoldersController>.Instance);

        var result = await controller.Get(cancellation.Token, "Movies");

        Assert.Same(listing, Assert.IsType<OkObjectResult>(result.Result).Value);
        await sender.Received(1).Send(
            Arg.Is<GetFolderQuery>(query => query.Path == "Movies"), cancellation.Token);
    }

    [Theory]
    [InlineData(FolderBrowserError.InvalidPath, 400)]
    [InlineData(FolderBrowserError.Forbidden, 403)]
    [InlineData(FolderBrowserError.NotFound, 404)]
    [InlineData(FolderBrowserError.Unavailable, 503)]
    public async Task ReturnsProblemDetailsForBrowsingErrors(FolderBrowserError error, int statusCode)
    {
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GetFolderQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<FolderListing>(new FolderBrowserException(error, "Cannot browse.")));
        var controller = new FoldersController(sender, NullLogger<FoldersController>.Instance);

        var result = await controller.Get(CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(statusCode, response.StatusCode);
        Assert.Equal("Cannot browse.", Assert.IsType<ProblemDetails>(response.Value).Title);
    }

    [Fact]
    public async Task PropagatesCancellation()
    {
        var sender = Substitute.For<ISender>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        sender.Send(Arg.Any<GetFolderQuery>(), cancellation.Token)
            .Returns(Task.FromCanceled<FolderListing>(cancellation.Token));
        var controller = new FoldersController(sender, NullLogger<FoldersController>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => controller.Get(cancellation.Token));
    }
}
