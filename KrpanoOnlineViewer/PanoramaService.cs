using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using System.Diagnostics;
using Serilog;

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
        return System.Text.Json.JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.ListPanoramaInfo) ??
               new List<PanoramaInfo>();
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

    private async Task<int> CheckImageAsync(string imagePath)
    {
        using Image image = await Image.LoadAsync(imagePath);
        int width = image.Width;
        int height = image.Height;
        if (width != height * 2)
        {
            throw new Exception("图像宽高比必须为2:1");
        }

        return width;
    }

    private async Task<bool> ConvertWithKrpanoAsync(string panoramaId, string inputFilePath, string outputDir,
        int imageWidth)
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
                throw new FileNotFoundException("没有找到krpanotools.exe文件", krpanoToolPath);
            }

            // 实际的krpano转换命令
            // 这里需要根据你的krpano版本和需求调整参数
            var arguments =
                $"makepano -outputpath=\"{outputDir}\" -maxsize={imageWidth} -maxcubesize={imageWidth} \"{inputFilePath}\"";

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
                    UpdateProgress(panoramaId, e.Data);
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    UpdateProgress(panoramaId, e.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "执行krpano转换失败");
            throw;
        }
    }

    private async Task ProcessPanoramaAsync(string panoramaId, string name, string outputDir, string sourceFile)
    {
        var status = processingStatus[panoramaId];

        try
        {
            // 2. 调用krpano转换
            status.Status = "正在检测图片信息";
            status.Progress = 40;

            int width = await CheckImageAsync(sourceFile);
            var success = await ConvertWithKrpanoAsync(panoramaId, sourceFile, outputDir, width);

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
                status.Error = "转换失败";
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
        var json = System.Text.Json.JsonSerializer.Serialize(panoramas,
            AppJsonSerializerContext.Default.ListPanoramaInfo);

        await File.WriteAllTextAsync(infoFile, json);
    }

    private void UpdateProgress(string panoramaId, string output)
    {
        Log.Information("krpano输出：{Output}", output);
        var status = processingStatus[panoramaId];
        status.Status = "转换中：" + output;
        if (output.Contains("loading"))
        {
            status.Progress = 50;
        }
        else if (output.Contains("making"))
        {
            status.Progress = 60;
        }
        else if (output.Contains("level"))
        {
            status.Progress = 80;
        }
    }

    // 修改全景图名称
    public async Task<bool> UpdatePanoramaNameAsync(string id, string newName)
    {
        var panoramas = await GetAllPanoramasAsync();
        var panorama = panoramas.FirstOrDefault(p => p.Id == id);

        if (panorama == null)
        {
            return false;
        }

        panorama.Name = newName;
        await SaveAllPanoramasAsync(panoramas);
        return true;
    }

// 删除全景图
    public async Task<bool> DeletePanoramaAsync(string id)
    {
        var panoramas = await GetAllPanoramasAsync();
        var panorama = panoramas.FirstOrDefault(p => p.Id == id);

        if (panorama == null)
        {
            return false;
        }

        // 从列表中移除
        panoramas.Remove(panorama);
        await SaveAllPanoramasAsync(panoramas);

        // 删除对应的文件目录
        var panoramaDir = Path.Combine(panoRootPath, id);
        if (Directory.Exists(panoramaDir))
        {
            try
            {
                Directory.Delete(panoramaDir, true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "删除全景图目录失败：{Id}", id);
            }
        }

        // 从处理状态中移除
        processingStatus.Remove(id);

        return true;
    }

// 保存全景图列表（重构现有方法）
    private async Task SaveAllPanoramasAsync(List<PanoramaInfo> panoramas)
    {
        var infoFile = Path.Combine(panoRootPath, "panoramas.json");
        var json = System.Text.Json.JsonSerializer.Serialize(panoramas,
            AppJsonSerializerContext.Default.ListPanoramaInfo);
        await File.WriteAllTextAsync(infoFile, json);
    }
}