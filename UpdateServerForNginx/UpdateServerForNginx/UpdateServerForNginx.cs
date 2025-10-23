using System.Security.Cryptography;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// --- Kestrel �ϴ����� ---
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200_000_000; // ~190 MB
});

var app = builder.Build();

// === ��̬�й�Ŀ¼��Nginx ��ȡ�� ===
// ��ȡ UpdateDir ���ã����û�о���Ĭ��·��
string updateDir = builder.Configuration["UpdateDir"];
if (string.IsNullOrWhiteSpace(updateDir))
{
    updateDir = @"D:/UpdateDir/var/www/updates"; // Windows ���Ƽ� /
}
Directory.CreateDirectory(updateDir);

string metaFile = Path.Combine(updateDir, "meta.json");

// �������а汾��Ϣ
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

// �� appsettings.json ��ȡ PublicBaseUrl
string configuredPublicBaseUrl = builder.Configuration["PublicBaseUrl"];
if (string.IsNullOrWhiteSpace(configuredPublicBaseUrl))
    throw new InvalidOperationException(
        "���� appsettings.json ������ PublicBaseUrl������ http://update.myserver.local"
    );

// ȥ��ĩβ�� / ����ƴ�� URL
configuredPublicBaseUrl = configuredPublicBaseUrl.TrimEnd('/');

// --- POST /upload �ӿ� ---
app.MapPost(
    "/upload",
    async (HttpRequest request) =>
    {
        var form = await request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        var versionStr = form["version"].ToString();

        if (file == null || string.IsNullOrEmpty(versionStr))
            return Results.BadRequest("ȱ���ļ���汾��");

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest("��֧�� ZIP �ļ��ϴ�");

        string appName = Path.GetFileNameWithoutExtension(file.FileName);

        // �汾��У���߼�
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
                        $"�汾����Ч���ϴ��汾 {versionStr} ���õ��ڵ�ǰ�汾 {existingVersionStr}"
                    );
                }
            }
        }

        // �����ļ��� Nginx ��̬Ŀ¼
        string savePath = Path.Combine(updateDir, Path.GetFileName(file.FileName));
        await using (var stream = File.Create(savePath))
        {
            await file.CopyToAsync(stream);
        }

        // ���� MD5
        string checksum;
        using (var md5 = MD5.Create())
        using (var fs = File.OpenRead(savePath))
        {
            checksum = BitConverter
                .ToString(md5.ComputeHash(fs))
                .Replace("-", "")
                .ToLowerInvariant();
        }

        // ���°汾��Ϣ
        meta[appName] = versionStr;

        await File.WriteAllTextAsync(
            metaFile,
            JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true })
        );

        // ���� update.json ����̬Ŀ¼
        GenerateUpdateJson(updateDir, configuredPublicBaseUrl, meta);

        string fileUrl =
            $"{configuredPublicBaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(file.FileName)}";
        Console.WriteLine($"�ϴ����: {file.FileName} �汾 {versionStr}");

        return Results.Ok(
            new
            {
                message = "�ϴ��ɹ�",
                name = appName,
                version = versionStr,
                fileName = file.FileName,
                exeName = appName + ".exe",
                checksum,
                url = fileUrl,
                notes = $"{appName} ���°汾 {versionStr}",
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
                notes = $"{name} ���°汾 {version}",
            }
        );
    }

    string json = JsonSerializer.Serialize(
        new { apps },
        new JsonSerializerOptions { WriteIndented = true }
    );
    File.WriteAllText(Path.Combine(dir, "update.json"), json);
}

// === �汾�űȽϸ������� ===
static bool TryParseVersion(string versionStr, out Version version)
{
    return Version.TryParse(versionStr.TrimStart('v', 'V'), out version);
}
