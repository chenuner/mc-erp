using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MC.Erp.Common.Errors;

/// <summary>
/// 把業務異常轉成 ProblemDetails（RFC 9457），endpoint 內不再寫 try/catch。
/// 未列出的異常照常拋出，由 UseExceptionHandler 統一處理。
/// </summary>
public sealed class ExceptionProblemFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (KeyNotFoundException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (UnauthorizedAccessException exception)
        {
            return TypedResults.Problem(
                string.IsNullOrWhiteSpace(exception.Message) ? "沒有權限執行此操作" : exception.Message,
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (InvalidOperationException exception)
        {
            return TypedResults.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TypedResults.Problem("資料已更新，請重新載入後重試", statusCode: StatusCodes.Status409Conflict);
        }
        catch (IOException)
        {
            return TypedResults.Problem("文件存儲操作失敗，請稍後重試", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}
