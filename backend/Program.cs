using System.Text;
using MC.Erp.Common;
using MC.Erp.Common.Security;
using MC.Erp.Common.Infrastructure.Ocr;
using MC.Erp.Features.Accounting.Endpoints;
using MC.Erp.Features.Accounting.Repositories;
using MC.Erp.Features.Accounting.Services;
using MC.Erp.Data;
using MC.Erp.Features.Attendance.Endpoints;
using MC.Erp.Features.Attendance.Repositories;
using MC.Erp.Features.Attendance.Services;
using MC.Erp.Features.Identity.Endpoints;
using MC.Erp.Features.Identity.Repositories;
using MC.Erp.Features.Identity.Services;
using MC.Erp.Features.Storage.Endpoints;
using MC.Erp.Features.Storage.Services;
using MC.Erp.Features.System.Endpoints;
using MC.Erp.Features.System.Repositories;
using MC.Erp.Features.System.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 日誌
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/mc-erp-.log", rollingInterval: RollingInterval.Day));

// DbContext
builder.Services.AddDbContext<ErpDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// 認證 & 授權
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
            NameClaimType = System.Security.Claims.ClaimTypes.Name,
            RoleClaimType = System.Security.Claims.ClaimTypes.Role,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true
        };
    });

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("admin", p => p.RequireRole("Admin"));
});

// 基礎設施
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.Configure<OcrOptions>(builder.Configuration.GetSection("Ocr"));
builder.Services.AddSingleton<IOcrService, AzureDocumentOcrService>();
builder.Services.AddScoped<ReceiptRepository>();
builder.Services.AddScoped<ReceiptService>();
builder.Services.AddScoped<AccountingFileProtectionService>();

// Identity 模塊
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRoleRepository, RoleRepository>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<RoleService>();

// Attendance 模塊
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();
builder.Services.AddScoped<AttendanceServices>();
builder.Services.AddScoped<IExternalAttendanceApi, ExternalAttendanceApi>();
builder.Services.AddScoped<AttendanceTableService>();

// Storage 模塊
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<FileService>();

// System 模塊
builder.Services.AddScoped<ISysDictRepository, SysDictRepository>();
builder.Services.AddScoped<SysDictService>();

// Hangfire
builder.Services.AddHangfire(config => config
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default"))));
builder.Services.AddHangfireServer();

// 健康檢查
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default")!);

// OpenAPI
builder.Services.AddOpenApi();

// 統一異常處理（ProblemDetails, RFC 9457）
builder.Services.AddProblemDetails();

var app = builder.Build();

// 異常處理
app.UseExceptionHandler();

// 認證 & 授權
app.UseAuthentication();
app.UseAuthorization();

// 健康檢查
app.MapHealthChecks("/health");

// API 文檔
// 禁止这操作，必须要在所有环境下都启用
// if (app.Environment.IsDevelopment())
// {
    app.MapOpenApi();
    app.MapScalarApiReference();
// }

// 端點
app.MapIdentityEndpoints();
app.MapAttendanceEndpoints();
app.MapSystemEndpoints();
app.MapStorageEndpoints();
app.MapAccountingEndpoints();

// 初始化種子數據
await app.Services.InitializeSeedDataAsync();

app.Run();
