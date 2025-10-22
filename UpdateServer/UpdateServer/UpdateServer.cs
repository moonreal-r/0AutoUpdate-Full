using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// === 更新文件目录 ===
string updateDir = Path.Combine(app.Environment.ContentRootPath, "Updates");
Directory.CreateDirectory(updateDir);
string metaFile = Path.Combine(updateDir, "meta.json");

// 载入已存在的版本信息
var meta = File.Exists(metaFile)
    ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(metaFile))!
    : new Dictionary<string, string>();

// 从 appsettings.json 读取 PublicBaseUrl
string configuredPublicBaseUrl = builder.Configuration["PublicBaseUrl"];
if (string.IsNullOrWhiteSpace(configuredPublicBaseUrl))
    throw new InvalidOperationException(
        "请在 appsettings.json 中配置 PublicBaseUrl，例如 http://192.168.9.100:5010"
    );

// === 启用静态文件访问 ===
app.UseStaticFiles(
    new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(updateDir),
        RequestPath = "",
    }
);

// --- GET /update.json ---
app.MapGet(
    "/update.json",
    () =>
    {
        var apps = new List<object>();
        var files = Directory.GetFiles(updateDir, "*.zip");

        string baseUrl = configuredPublicBaseUrl.TrimEnd('/'); // 直接使用配置

        foreach (var file in files)
        {
            string fileName = Path.GetFileName(file);
            string name = Path.GetFileNameWithoutExtension(fileName);
            string exeName = name + ".exe";
            long size = new FileInfo(file).Length;

            string checksum;
            using (var md5 = System.Security.Cryptography.MD5.Create())
            using (var stream = File.OpenRead(file))
            {
                var hash = md5.ComputeHash(stream);
                checksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }

            string version = meta.ContainsKey(name) ? meta[name] : "1.0.0.0";

            string url = $"{baseUrl}/{Uri.EscapeDataString(fileName)}";

            apps.Add(
                new
                {
                    name,
                    version,
                    fileName,
                    exeName,
                    size,
                    checksum,
                    url,
                    notes = $"{name} 最新版本 {version}",
                }
            );
        }

        string json = JsonSerializer.Serialize(
            new { apps },
            new JsonSerializerOptions { WriteIndented = true }
        );
        return Results.Content(json, "application/json");
    }
);

app.MapPost(
    "/upload",
    async (HttpRequest request) =>
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var version = form["version"].ToString();

        if (file == null || string.IsNullOrEmpty(version))
            return Results.BadRequest("缺少文件或版本号");

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("仅支持 ZIP 文件上传");

        // 保存文件
        string savePath = Path.Combine(updateDir, Path.GetFileName(file.FileName));
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream);
        }

        // 计算 MD5 校验
        string checksum;
        using (var md5 = System.Security.Cryptography.MD5.Create())
        using (var fs = File.OpenRead(savePath))
        {
            var hash = md5.ComputeHash(fs);
            checksum = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // 更新版本信息
        string appName = Path.GetFileNameWithoutExtension(file.FileName);
        meta[appName] = version;
        await File.WriteAllTextAsync(
            metaFile,
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })
        );

        // --- 使用 appsettings.json 配置生成下载 URL ---
        string baseUrl = configuredPublicBaseUrl.TrimEnd('/');
        string fileUrl = $"{baseUrl}/{Uri.EscapeDataString(file.FileName)}";

        Console.WriteLine($"上传完成: {file.FileName} 版本 {version}");

        return Results.Ok(
            new
            {
                message = "上传成功",
                name = appName,
                version,
                fileName = file.FileName,
                exeName = appName + ".exe",
                checksum,
                url = fileUrl,
                notes = $"{appName} 最新版本 {version}",
            }
        );
    }
);

app.Run("http://0.0.0.0:5010");
