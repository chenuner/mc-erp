using MC.Erp.Features.System.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MC.Erp.Features.System.Endpoints;

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/system").WithTags("System");

        // 系統字典端點可在此添加
        // 例：group.MapGet("/dict/{groupName}", GetSysDictAsync).RequireAuthorization();
    }
}
