using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 更新文件目录（存放 ZIP + JSON）
string updateDir = Path.Combine(app.Environment.ContentRootPath, "Updates");
Directory.CreateDirectory(updateDir);

// 启用静态文件访问
app.UseStaticFiles(
    new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(updateDir),
        RequestPath = "",
    }
);

// GET: 返回 update.json
app.MapGet(
    "/update.json",
    async (HttpRequest request) =>
    {
        string jsonPath = Path.Combine(updateDir, "update.json");
        if (!File.Exists(jsonPath))
            return Results.NotFound(new { error = "update.json not found" });

        string json = await File.ReadAllTextAsync(jsonPath);
        return Results.Content(json, "application/json");
    }
);

// POST: 上传新版本
app.MapPost(
    "/upload",
    async (HttpRequest request) =>
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var version = form["version"].ToString();

        if (file == null || string.IsNullOrEmpty(version))
            return Results.BadRequest("缺少文件或版本号");

        // 保存 ZIP
        string savePath = Path.Combine(updateDir, Path.GetFileName(file.FileName));
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream);
        }

        // 重新生成 update.json
        var apps = new List<object>();
        var files = Directory.GetFiles(updateDir, "*.zip");
        foreach (var f in files)
        {
            string fileName = Path.GetFileName(f);
            string name = Path.GetFileNameWithoutExtension(fileName);
            string exeName = name + ".exe";
            long size = new FileInfo(f).Length;

            // MD5 校验
            string checksum;
            using (var md5 = MD5.Create())
            using (var stream = File.OpenRead(f))
            {
                checksum = BitConverter
                    .ToString(md5.ComputeHash(stream))
                    .Replace("-", "")
                    .ToLowerInvariant();
            }

            // 生成可访问 URL
            string url = $"http://{request.Host}/{fileName}";

            apps.Add(
                new
                {
                    name,
                    version = version,
                    fileName,
                    url,
                    exeName,
                    size,
                    checksum,
                    notes = $"{name} 上传",
                }
            );
        }

        string jsonPathOut = Path.Combine(updateDir, "update.json");
        string jsonContent = JsonSerializer.Serialize(
            new { apps },
            new JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(jsonPathOut, jsonContent);

        return Results.Ok(
            new
            {
                message = "上传成功",
                fileName = file.FileName,
                version,
            }
        );
    }
);

app.Run("http://0.0.0.0:5010");
