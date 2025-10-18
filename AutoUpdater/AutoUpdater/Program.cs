using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class AutoUpdater
{
    private static readonly string UpdateServerUrl = "http://192.168.16.52:5010/update.json";
    private static readonly HttpClient http = new();

    static async Task Main()
    {
        string appDir = AppContext.BaseDirectory;
        string downloadDir = Path.Combine(appDir, "Download");
        Directory.CreateDirectory(downloadDir);

        string localVersionFile = Path.Combine(appDir, "local_version.txt");

        // 获取服务器 JSON
        var json = await http.GetStringAsync(UpdateServerUrl);
        var updateList = JsonSerializer.Deserialize<UpdateList>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        )!;

        // 找到当前软件信息
        var appName = Path.GetFileName(appDir);
        var app = updateList.apps.Find(a =>
            a.name.Equals(appName, StringComparison.OrdinalIgnoreCase)
        );
        if (app == null)
        {
            Console.WriteLine("未在服务器找到软件信息！");
            return;
        }

        string localVersion = File.Exists(localVersionFile)
            ? File.ReadAllText(localVersionFile)
            : "0.0.0.0";

        string softwareDir = Path.Combine(appDir, app.name); // 两层目录：FSCapture\FSCapture
        string exePath = Path.Combine(softwareDir, app.exeName);

        // 更新逻辑
        if (localVersion != app.version)
        {
            Console.WriteLine($"检测到新版本 {app.version}，正在更新...");

            string zipPath = Path.Combine(downloadDir, app.fileName);
            await DownloadFileAsync(app.url, zipPath, app.name);

            // 清理软件目录，不删除上层 appDir
            if (Directory.Exists(softwareDir))
                Directory.Delete(softwareDir, true);

            ZipFile.ExtractToDirectory(zipPath, softwareDir, overwriteFiles: true);

            File.WriteAllText(localVersionFile, app.version);

            Console.WriteLine("更新完成！");
        }
        else
        {
            Console.WriteLine("已是最新版本。");
        }

        // 启动软件
        if (File.Exists(exePath))
        {
            Console.WriteLine("启动软件...");
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });
        }
        else
        {
            Console.WriteLine($"找不到主程序 exe：{exePath}");
        }
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

    private static void CleanDownloadFolder(string downloadDir)
    {
        if (Directory.Exists(downloadDir))
        {
            try
            {
                Directory.Delete(downloadDir, true);
                Directory.CreateDirectory(downloadDir); // 保留空目录
                Console.WriteLine("[Download] 临时文件夹已清理");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Download] 清理失败: " + ex.Message);
            }
        }
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
