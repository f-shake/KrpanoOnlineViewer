using KrpanoOnlineViewer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Routing.Constraints;
using Serilog;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.AddSingleton<PanoramaService>();
builder.Host.UseWindowsService();
builder.Host.UseSerilog();
InitializeLogger(builder);

//CreateSlimBuilder下需要加这些才能适配Swagger
builder.Services.Configure<RouteOptions>(options =>
{
    options.SetParameterPolicy<RegexInlineRouteConstraint>("regex");
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 524288000; // 500MB
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000; // 500MB
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

var app = builder.Build();

InitializeGlobalExceptionHandler(app);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors(policy => policy
        .AllowAnyOrigin()
        .AllowAnyMethod()
        .AllowAnyHeader());
}

ConfigureAccessPassword(app, builder);

InitializeStaticFiles(app);

// API 路由
var api = app.MapGroup("/api");
api.MapGet("/panoramas", GetAllPanoromas).WithOpenApi();
api.MapPost("/upload", UploadFile).DisableAntiforgery();
api.MapGet("/status/{id}", GetStatus).WithOpenApi();

api.MapPut("/panoramas/{id}", RenameAsync).WithOpenApi();

// 删除全景图
api.MapDelete("/panoramas/{id}", DeleteFileAsync).WithOpenApi();
app.Run();

async Task<IResult> RenameAsync(string id, [FromBody] PanoramaInfo request, PanoramaService panoramaService)
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest("名称不能为空");
    }

    var success = await panoramaService.UpdatePanoramaNameAsync(id, request.Name.Trim());
    return success ? Results.Ok() : Results.NotFound();
}

async Task<IResult> DeleteFileAsync(string id, PanoramaService panoramaService)
{
    var success = await panoramaService.DeletePanoramaAsync(id);
    return success ? Results.Ok() : Results.NotFound();
}

async Task<IResult> UploadFile(HttpContext context, PanoramaService panoramaService, IFormFile file)
{
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("没有上传文件");
    }

    if (file.Length > 500 * 1024 * 1024)
    {
        return Results.BadRequest("文件大小过大（限制为500MB）");
    }

    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowedExtensions.Contains(extension))
    {
        return Results.BadRequest("不支持的文件格式");
    }

    var panoramaId = await panoramaService.StartProcessingAsync(file);
    return Results.Ok(new PanoramaInfo { Id = panoramaId });
}

async Task<IResult> GetStatus(string id, PanoramaService panoramaService)
{
    var status = await panoramaService.GetStatusAsync(id);
    return status != null ? Results.Ok(status) : Results.NotFound();
}

static void InitializeStaticFiles(WebApplication app)
{
    string panoDir = Path.Combine(AppContext.BaseDirectory, "wwwroot", "panoramas");
    Directory.CreateDirectory(panoDir);
    app.UseStaticFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(panoDir),
        RequestPath = "/panoramas"
    });
    app.UseDefaultFiles();
    app.MapFallback(async context =>
    {
        if (context.Request.Path == "/")
        {
            var indexPath = Path.Combine(AppContext.BaseDirectory, "wwwroot", "index.html");
            if (File.Exists(indexPath))
            {
                context.Response.ContentType = "text/html";
                await context.Response.SendFileAsync(indexPath);
                return;
            }
        }

        // 对于其他路径，返回 404
        context.Response.StatusCode = 404;
    });
}

async Task<IResult> GetAllPanoromas(PanoramaService panoramaService)
{
    var panoramas = await panoramaService.GetAllPanoramasAsync();
    return Results.Ok(panoramas);
}

void ConfigureAccessPassword(WebApplication webApplication, WebApplicationBuilder webApplicationBuilder)
{
    webApplication.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            var configPassword = webApplicationBuilder.Configuration["AccessPassword"];
            if (!string.IsNullOrEmpty(configPassword))
            {
                var requestPassword = context.Request.Headers["X-Access-Token"].FirstOrDefault();

                if (requestPassword != configPassword)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized: 密码错误");
                    return;
                }
            }
        }

        await next();
    });
}


void InitializeLogger(WebApplicationBuilder builder)
{
    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .WriteTo.File(
            Path.Combine(AppContext.BaseDirectory, "logs", "app.log"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 7)
        .WriteTo.Console()
        .CreateLogger();

    Log.Information("logger初始化完成");
}


void InitializeGlobalExceptionHandler(WebApplication app)
{
    app.Use(async (context, next) =>
    {
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "全局未捕获异常：{Path}", context.Request.Path);

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsync(ex.Message);
        }
    });
}