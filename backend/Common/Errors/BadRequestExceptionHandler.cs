using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Common.Errors;

/// <summary>
/// 查詢字串／路由參數綁定失敗（例如 <c>?siteDictId=</c> 或格式不對）由 Minimal API 在端點執行前拋出
/// <see cref="BadHttpRequestException"/>，端點自己的 <see cref="ExceptionProblemFilter"/> 攔不到。
/// 這裡全域轉成 400 ProblemDetails，前端只會看到一句「參數無效」，不會變成 500。
/// </summary>
public sealed class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest) return false;
        logger.LogWarning("請求參數無效：{Message}", badRequest.Message);
        await TypedResults.Problem("請求參數無效，請檢查查詢條件。", statusCode: StatusCodes.Status400BadRequest)
            .ExecuteAsync(httpContext);
        return true;
    }
}
