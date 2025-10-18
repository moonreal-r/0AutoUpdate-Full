using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class MainClient
{
    private static readonly string UpdateServerUrl = "http://localhost:5010/update.json";
    private static readonly string DownloadFolder = Path.Combine(
        AppContext.BaseDirectory,
        "Downloads"
    );
    private static readonly string RootInstallDir = @"D:\Apps";
    private static readonly string LocalVersionFile = Path.Combine(
        AppContext.BaseDirectory,
        "client_version.txt"
    );
    private static readonly HttpClient http = new();

    static async Task Main()
    {
        Directory.CreateDirectory(DownloadFolder);
        Directory.CreateDirectory(RootInstallDir);

        Console.WriteLine("检查主客户端更新...");
        var json = await http.GetStringAsync(UpdateServerUrl);
        var updateList = JsonSerializer.Deserialize<UpdateList>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        )!;

        // 检查主客户端自身更新
        var clientApp = updateList.apps.Find(a =>
            a.name.Equals("MainClient", StringComparison.OrdinalIgnoreCase)
        );
        if (clientApp != null)
        {
            string localVersion = File.Exists(LocalVersionFile)
                ? File.ReadAllText(LocalVersionFile)
                : "0.0.0.0";
            if (localVersion != clientApp.version)
            {
                Console.WriteLine($"检测到主客户端新版本 {clientApp.version}，正在更新...");
                await DownloadAndExtract(clientApp.url, AppContext.BaseDirectory);
                File.WriteAllText(LocalVersionFile, clientApp.version);
                Console.WriteLine("主客户端更新完成！");
            }
            else
            {
                Console.WriteLine("主客户端已是最新版本。");
            }
        }

        // 下载每个软件及其 AutoUpdater
        var updaterApp = updateList.apps.Find(a =>
            a.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase)
        );
        if (updaterApp == null)
        {
            Console.WriteLine("未找到 AutoUpdater 信息！");
            return;
        }

        foreach (var app in updateList.apps)
        {
            if (
                app.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase)
                || app.name.Equals("MainClient", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            string appDir = Path.Combine(RootInstallDir, app.name);
            string updaterExe = Path.Combine(appDir, "AutoUpdater.exe");

            if (!Directory.Exists(appDir) || !File.Exists(updaterExe))
            {
                Console.WriteLine($"\n[{app.name}] 未安装，是否下载? (Y/N)");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                {
                    await DownloadSoftwareWithUpdater(app, appDir, updaterApp);
                    Console.WriteLine(
                        $"[{app.name}] 下载完成！请使用 {updaterExe} 启动软件并自动更新。"
                    );
                }
                else
                {
                    Console.WriteLine($"[{app.name}] 跳过下载。");
                }
            }
        }

        Console.WriteLine("\n首次安装检查完成。");
    }

    private static async Task DownloadSoftwareWithUpdater(
        UpdateInfo app,
        string appDir,
        UpdateInfo updaterApp
    )
    {
        Directory.CreateDirectory(appDir);

        // 1️⃣ 下载软件 ZIP 到临时文件夹
        string zipPath = Path.Combine(DownloadFolder, app.fileName);
        await DownloadFileAsync(app.url, zipPath, app.name);

        // 2️⃣ 解压到软件子目录（两层目录）
        string softwareDir = Path.Combine(appDir, app.name);
        if (Directory.Exists(softwareDir))
            Directory.Delete(softwareDir, true);
        ZipFile.ExtractToDirectory(zipPath, softwareDir, overwriteFiles: true);

        // 3️⃣ 下载 AutoUpdater ZIP
        string updaterZipPath = Path.Combine(DownloadFolder, updaterApp.fileName);
        await DownloadFileAsync(updaterApp.url, updaterZipPath, "AutoUpdater");

        // 4️⃣ 解压 AutoUpdater 到软件根目录
        ZipFile.ExtractToDirectory(updaterZipPath, appDir, overwriteFiles: true);

        Console.WriteLine($"[{app.name}] 软件和 AutoUpdater 已安装完成。");
    }

    private static async Task DownloadAndExtract(string url, string targetDir)
    {
        string zipPath = Path.Combine(DownloadFolder, Path.GetFileName(url));
        await DownloadFileAsync(url, zipPath, "MainClient");
        // 注意：主客户端不能在自己运行目录直接删除自身，通常需要提示用户重启更新
        ZipFile.ExtractToDirectory(zipPath, targetDir, overwriteFiles: true);
    }

    private static async Task DownloadFileAsync(string url, string destPath, string appName)
    {
        Console.WriteLine($"[{appName}] 下载中: {url}");

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        var canReportProgress = totalBytes != -1;

        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;

        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read);
            totalRead += read;

            if (canReportProgress)
            {
                Console.Write($"\r[{appName}] 下载进度: {totalRead * 100 / totalBytes}%   ");
            }
        }

        Console.WriteLine($"\n[{appName}] 下载完成: {destPath}");
    }

    private class UpdateList
    {
        public System.Collections.Generic.List<UpdateInfo> apps { get; set; } = new();
    }

    private class UpdateInfo
    {
        public string name { get; set; } = "";
        public string version { get; set; } = "";
        public string fileName { get; set; } = "";
        public string url { get; set; } = "";
        public string exeName { get; set; } = "";
    }
}
