using System.Security.Cryptography;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// --- Kestrel 上传限制 ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200_000_000; // ~190 MB
});

var app = builder.Build();

// === 静态托管目录（Nginx 读取） ===
// 读取 UpdateDir 配置，如果没有就用默认路径
string updateDir = builder.Configuration["UpdateDir"];
if (string.IsNullOrWhiteSpace(updateDir))
{
    updateDir = @"D:/UpdateDir/var/www/updates"; // Windows 下推荐 /
}
Directory.CreateDirectory(updateDir);

string metaFile = Path.Combine(updateDir, "meta.json");

// 载入已有版本信息
Dictionary<string, string> meta;
if (File.Exists(metaFile))
{
    var json = File.ReadAllText(metaFile);
    meta =
        JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? new Dictionary<string, string>();
}
else
{
    meta = new Dictionary<string, string>();
}

// 从 appsettings.json 获取 PublicBaseUrl
string configuredPublicBaseUrl = builder.Configuration["PublicBaseUrl"];
if (string.IsNullOrWhiteSpace(configuredPublicBaseUrl))
    throw new InvalidOperationException(
        "请在 appsettings.json 中配置 PublicBaseUrl，例如 http://update.myserver.local"
    );

// 去掉末尾的 / 方便拼接 URL
configuredPublicBaseUrl = configuredPublicBaseUrl.TrimEnd('/');

// --- POST /upload 接口 ---
app.MapPost(
    "/upload",
    async (HttpRequest request) =>
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var versionStr = form["version"].ToString();

        if (file == null || string.IsNullOrEmpty(versionStr))
            return Results.BadRequest("缺少文件或版本号");

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("仅支持 ZIP 文件上传");

        string appName = Path.GetFileNameWithoutExtension(file.FileName);

        // 版本号校验逻辑
        if (meta.TryGetValue(appName, out string existingVersionStr))
        {
            if (
                TryParseVersion(existingVersionStr, out var existingVersion)
                && TryParseVersion(versionStr, out var newVersion)
            )
            {
                if (newVersion <= existingVersion)
                {
                    return Results.BadRequest(
                        $"版本号无效：上传版本 {versionStr} 不得低于当前版本 {existingVersionStr}"
                    );
                }
            }
        }

        // 保存文件到 Nginx 静态目录
        string savePath = Path.Combine(updateDir, Path.GetFileName(file.FileName));
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream);
        }

        // 计算 MD5
        string checksum;
        using (var md5 = MD5.Create())
        using (var fs = File.OpenRead(savePath))
        {
            checksum = BitConverter
                .ToString(md5.ComputeHash(fs))
                .Replace("-", "")
                .ToLowerInvariant();
        }

        // 更新版本信息
        meta[appName] = versionStr;

        await File.WriteAllTextAsync(
            metaFile,
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })
        );

        // 生成 update.json 到静态目录
        GenerateUpdateJson(updateDir, configuredPublicBaseUrl, meta);

        string fileUrl =
            $"{configuredPublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(file.FileName)}";
        Console.WriteLine($"上传完成: {file.FileName} 版本 {versionStr}");

        return Results.Ok(
            new
            {
                message = "上传成功",
                name = appName,
                version = versionStr,
                fileName = file.FileName,
                exeName = appName + ".exe",
                checksum,
                url = fileUrl,
                notes = $"{appName} 最新版本 {versionStr}",
            }
        );
    }
);

app.Run("http://0.0.0.0:5010");

void GenerateUpdateJson(string dir, string baseUrl, Dictionary<string, string> metaDict)
{
    var apps = new List<object>();
    var files = Directory.GetFiles(dir, "*.zip");

    string trimmedBaseUrl = baseUrl.TrimEnd('/');

    foreach (var file in files)
    {
        string fileName = Path.GetFileName(file);
        string name = Path.GetFileNameWithoutExtension(fileName);
        string exeName = name + ".exe";
        long size = new FileInfo(file).Length;

        string checksum;
        using (var md5 = MD5.Create())
        using (var fs = File.OpenRead(file))
        {
            checksum = BitConverter
                .ToString(md5.ComputeHash(fs))
                .Replace("-", "")
                .ToLowerInvariant();
        }

        string version = metaDict.ContainsKey(name) ? metaDict[name] : "1.0.0.0";
        string url = $"{trimmedBaseUrl}/{Uri.EscapeDataString(fileName)}";

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
    File.WriteAllText(Path.Combine(dir, "update.json"), json);
}

// === 版本号比较辅助函数 ===
static bool TryParseVersion(string versionStr, out Version version)
{
    return Version.TryParse(versionStr.TrimStart('v', 'V'), out version);
}
