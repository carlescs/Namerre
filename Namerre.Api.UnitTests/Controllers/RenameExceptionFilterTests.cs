using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Namerre.Application.Renaming;
using Namerre.Controllers;
using Xunit;

namespace Namerre.Api.UnitTests.Controllers;

public sealed class RenameExceptionFilterTests
{
    private readonly RenameExceptionFilter filter = new(NullLogger<RenameExceptionFilter>.Instance);

    [Fact]
    public void RenameExceptionsBecomeProblemDetailsWithTheirCodeAndStatus()
    {
        var context = Context(new RenameException("PlanNotFound", "The plan does not exist.", 404, "f1"));

        filter.OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.True(context.ExceptionHandled);
        Assert.Equal(404, result.StatusCode);
        Assert.Equal(404, problem.Status);
        Assert.Equal("The plan does not exist.", problem.Title);
        Assert.Equal("PlanNotFound", problem.Extensions["code"]);
    }

    [Fact]
    public void RenameExceptionsDefaultToBadRequest()
    {
        var context = Context(new RenameException("BatchLimit", "Too many files."));

        filter.OnException(context);

        Assert.Equal(400, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(OperationCanceledException))]
    public void UnexpectedFailuresHideDetailsBehindServiceUnavailable(Type exceptionType)
    {
        var context = Context((Exception)Activator.CreateInstance(exceptionType, "secret internals")!);

        filter.OnException(context);

        var result = Assert.IsType<ObjectResult>(context.Result);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(503, result.StatusCode);
        Assert.Equal("RenameUnavailable", problem.Extensions["code"]);
        Assert.DoesNotContain("secret", problem.Title);
        Assert.True(context.ExceptionHandled);
    }

    [Fact]
    public void ClientAbortedCancellationIsLeftUnhandled()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = Context(new OperationCanceledException(), aborted.Token);

        filter.OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }

    private static ExceptionContext Context(Exception exception, CancellationToken requestAborted = default)
    {
        var http = new DefaultHttpContext { RequestAborted = requestAborted };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ExceptionContext(action, []) { Exception = exception };
    }
}
