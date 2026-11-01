using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Namerre.Application.Authentication;

namespace Namerre.Controllers;

/// <summary>Returns refused account changes as <c>{ code, message }</c> and storage failures as 503.</summary>
public sealed class AccountExceptionFilter(ILogger<AccountExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case AccountException exception:
                context.Result = new ObjectResult(new { code = exception.Code, message = exception.Message })
                {
                    StatusCode = exception.Status
                };
                context.ExceptionHandled = true;
                break;
            case UserStorageException exception:
                logger.LogError(exception, "Unable to save users.");
                context.Result = new ObjectResult(new
                {
                    code = "StorageUnavailable",
                    message = "Unable to save the change. Check the database and server permissions, then retry."
                }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
                context.ExceptionHandled = true;
                break;
        }
    }
}
