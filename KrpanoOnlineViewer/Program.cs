using KrpanoOnlineViewer;
using Microsoft.Extensions.FileProviders;
using System.Text.Json.Serialization.Metadata;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors();
builder.Services.AddSingleton<PanoramaService>();

if (builder.Environment.IsProduction())
{
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
    });
}

var app = builder.Build();

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

app.UseHttpsRedirection();

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

// API 路由
var api = app.MapGroup("/api");

api.MapGet("/panoramas", async (PanoramaService panoramaService) =>
{
    var panoramas = await panoramaService.GetAllPanoramasAsync();
    return Results.Ok(panoramas);
})
    .WithOpenApi();

api.MapPost("/upload", async (HttpContext context, PanoramaService panoramaService, IFormFile file) =>
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
})
    .DisableAntiforgery();

api.MapGet("/status/{id}", async (string id, PanoramaService panoramaService) =>
{
    var status = await panoramaService.GetStatusAsync(id);
    return status != null ? Results.Ok(status) : Results.NotFound();
})
    .WithOpenApi();

app.Run();
