using Microsoft.Extensions.Configuration;
using System.Diagnostics;

namespace KrpanoOnlineViewer;

public class PanoramaService
{
    private readonly IConfiguration configuration;

    private readonly string panoRootPath;

    private readonly Dictionary<string, ProcessingStatus> processingStatus;

    public PanoramaService(IConfiguration configuration)
    {
        var basePath = AppContext.BaseDirectory;
        panoRootPath = Path.Combine(basePath, "wwwroot", "panoramas");
        processingStatus = new Dictionary<string, ProcessingStatus>();

        // 确保目录存在
        Directory.CreateDirectory(panoRootPath);
        this.configuration = configuration;
    }

    public async Task<List<PanoramaInfo>> GetAllPanoramasAsync()
    {
        var infoFile = Path.Combine(panoRootPath, "panoramas.json");
        if (!File.Exists(infoFile))
            return new List<PanoramaInfo>();

        var json = await File.ReadAllTextAsync(infoFile);
        return System.Text.Json.JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListPanoramaInfo) ?? new List<PanoramaInfo>();
    }

    public Task<ProcessingStatus?> GetStatusAsync(string id)
    {
        processingStatus.TryGetValue(id, out var status);
        return Task.FromResult(status);
    }

    public async Task<string> StartProcessingAsync(IFormFile file)
    {
        var panoramaId = Guid.NewGuid().ToString("N");
        var status = new ProcessingStatus
        {
            Id = panoramaId,
            OriginalFileName = file.FileName,
            Status = "上传中",
            Progress = 0
        };

        processingStatus[panoramaId] = status;

        // 1. 保存原文件
        status.Status = "正在保存";
        status.Progress = 10;
        string name = Path.GetFileNameWithoutExtension(file.FileName);
        var dir = Path.Combine(panoRootPath, $"{panoramaId}");
        var sourceFile = Path.Combine(dir, "source" + Path.GetExtension(file.FileName));
        Directory.CreateDirectory(dir);
        using (var stream = new FileStream(sourceFile, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        // 在后台处理
        _ = Task.Run(() => ProcessPanoramaAsync(panoramaId, name, dir, sourceFile));

        return panoramaId;
    }

    private async Task<bool> ConvertWithKrpanoAsync(string panoramaId, string inputFilePath, string outputDir)
    {
        try
        {

            var krpanoToolPath = configuration.GetValue<string>("KrpanoExe");

            if (krpanoToolPath == null)
            {
                throw new InvalidOperationException("没有设置krpanotools.exe的位置，请在appsettings.json中进行设置");
            }

            if (!File.Exists(krpanoToolPath))
            {
                throw new FileNotFoundException("Krpano tool not found", krpanoToolPath);
            }

            // 实际的krpano转换命令
            // 这里需要根据你的krpano版本和需求调整参数
            var arguments = $"makepano -outputpath=\"{outputDir}\" \"{inputFilePath}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = krpanoToolPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return false;
            }

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Console.WriteLine(e.Data);
                    processingStatus[panoramaId].Status = "转换中：" + e.Data;
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Console.WriteLine(e.Data);
                    processingStatus[panoramaId].Status = "转换中：" + e.Data;
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }

    private async Task ProcessPanoramaAsync(string panoramaId, string name, string outputDir, string sourceFile)
    {
        var status = processingStatus[panoramaId];

        try
        {
            // 2. 调用krpano转换
            status.Status = "转换中";
            status.Progress = 30;

            var success = await ConvertWithKrpanoAsync(panoramaId, sourceFile, outputDir);

            if (success)
            {
                status.Status = "completed";
                status.Progress = 100;
                status.OutputPath = outputDir;
                status.CompletedAt = DateTime.UtcNow;

                // 保存全景信息
                var panoramaInfo = new PanoramaInfo
                {
                    Id = panoramaId,
                    Name = name,
                    CreatedAt = DateTime.UtcNow,
                };

                await SavePanoramaInfoAsync(panoramaInfo);
            }
            else
            {
                status.Status = "error";
                status.Progress = 0;
                status.Error = "KRpano conversion failed";
            }
        }
        catch (Exception ex)
        {
            status.Status = "error";
            status.Error = ex.Message;
        }
    }
 
    private async Task SavePanoramaInfoAsync(PanoramaInfo info)
    {
        var panoramas = await GetAllPanoramasAsync();
        panoramas.Add(info);

        var infoFile = Path.Combine(panoRootPath, "panoramas.json");
        var json = System.Text.Json.JsonSerializer.Serialize(panoramas, AppJsonSerializerContext.Default.ListPanoramaInfo);

        await File.WriteAllTextAsync(infoFile, json);
    }
}
