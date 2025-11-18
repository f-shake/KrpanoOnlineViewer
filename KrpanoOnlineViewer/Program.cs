using KrpanoOnlineViewer;
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
var panoDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
Directory.CreateDirectory(panoDir);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(panoDir),
    RequestPath = "/panoramas"
});
app.UseDefaultFiles();

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
        return Results.BadRequest("No file uploaded");

    if (file.Length > 500 * 1024 * 1024) // 500MB限制
        return Results.BadRequest("File too large");

    var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff" };
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    if (!allowedExtensions.Contains(extension))
        return Results.BadRequest("Invalid file format");

    var panoramaId = await panoramaService.StartProcessingAsync(file);
    return Results.Ok(new { id = panoramaId });
})
    .DisableAntiforgery();

api.MapGet("/status/{id}", async (string id, PanoramaService panoramaService) =>
{
    var status = await panoramaService.GetStatusAsync(id);
    return status != null ? Results.Ok(status) : Results.NotFound();
})
    .WithOpenApi();

app.Run();
