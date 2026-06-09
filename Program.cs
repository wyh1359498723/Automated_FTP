using Automated_FTP.Infrastructure.Email;
using Automated_FTP.Repositories;
using Automated_FTP.Services;
using Automated_FTP.Services.Ftp;
using Automated_FTP.Services.Processors;
using Automated_FTP.Services.Renamers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("DevCors", policy =>
        policy
            .SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Automated FTP API", Version = "v1" });
});

// 配置选项
builder.Services.Configure<OracleOptions>(builder.Configuration.GetSection("Oracle"));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Upload"));
builder.Services.Configure<Automated_FTP.Infrastructure.Email.EmailAlertOptions>(
    builder.Configuration.GetSection("EmailAlert"));

// 仓储
builder.Services.AddScoped<IConfigRepository, OracleConfigRepository>();

// 工具组件
builder.Services.AddSingleton<PathTemplateRenderer>();
builder.Services.AddSingleton<FileFinder>();
builder.Services.AddSingleton<FileTransferClientFactory>();

// 文件处理策略
builder.Services.AddSingleton<IFileProcessor, PassThroughProcessor>();
builder.Services.AddSingleton<IFileProcessor, ZipCompressProcessor>();
builder.Services.AddSingleton<IFileProcessor, ZipCompressAllProcessor>();
builder.Services.AddSingleton<FileProcessorRegistry>();

// 改名策略
builder.Services.AddSingleton<IFileRenamer, TemplateRenamer>();
builder.Services.AddSingleton<IFileRenamer, KeepOriginalRenamer>();
builder.Services.AddSingleton<FileRenamerRegistry>();

builder.Services.AddSingleton<IAlertEmailService, SmtpAlertEmailService>();

// 主编排
builder.Services.AddScoped<IFtpUploadService, FtpUploadService>();

var app = builder.Build();

app.UseCors("DevCors");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();

// 兜底异常处理：把未捕获异常包装成统一 JSON 错误结构。
app.Use(async (ctx, next) =>
{
    try
    {
        await next();
    }
    catch (BadHttpRequestException ex)
    {
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsJsonAsync(new { success = false, error = ex.Message });
        }
    }
    catch (Exception ex)
    {
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("UnhandledException");
        logger.LogError(ex, "请求处理失败 {Path}", ctx.Request.Path);
        if (!ctx.Response.HasStarted)
        {
            ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsJsonAsync(new
            {
                success = false,
                error = ex.Message,
                type = ex.GetType().FullName,
            });
        }
    }
});

app.MapControllers();

app.Run();
