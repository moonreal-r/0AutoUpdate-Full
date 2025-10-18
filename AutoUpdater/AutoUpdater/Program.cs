using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class AutoUpdater
{
    private static readonly HttpClient http = new();
    private static readonly string DefaultRootConfigUrl = "http://192.168.9.100:5010/updater.json";

    static async Task Main(string[] args)
    {
        string appDir = AppContext.BaseDirectory;
        string exePath = Path.Combine(appDir, "AutoUpdater.exe");
        string localVersionFile = Path.Combine(appDir, "local_version.txt");
        string tempDir = Path.Combine(appDir, "Temp");

        // --- 临时实例模式：负责替换文件 ---
        if (args.Length >= 2 && args[0] == "--replace")
        {
            string newVersion = args[1];
            Console.WriteLine("临时实例：开始替换文件...");

            foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(tempDir, file);
                string dest = Path.Combine(appDir, relative);

                string destDir = Path.GetDirectoryName(dest)!;
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                File.Copy(file, dest, true);
            }

            // 写入版本号
            File.WriteAllText(localVersionFile, newVersion);

            Console.WriteLine("自我更新完成，启动新版本 AutoUpdater...");
            Process.Start(new ProcessStartInfo(exePath) { UseShellExecute = true });

            // 清理 Temp
            Directory.Delete(tempDir, true);
            return;
        }

        // --- 获取更新服务器 ---
        string updateServerUrl = await ResolveUpdateServerUrl();
        if (updateServerUrl == null)
        {
            Console.WriteLine("无法获取更新服务器地址，程序退出。");
            return;
        }

        Console.WriteLine($"使用更新服务器: {updateServerUrl}");

        // --- 拉取 update.json ---
        string json;
        try
        {
            json = await http.GetStringAsync(updateServerUrl);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"拉取 update.json 失败: {ex.Message}");
            return;
        }

        var updateList = JsonSerializer.Deserialize<UpdateList>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        )!;

        // --- 自我更新 ---
        string localVersion = File.Exists(localVersionFile)
            ? File.ReadAllText(localVersionFile)
            : "0.0.0.0";
        var selfApp = updateList.apps.Find(a =>
            string.Equals(a.name, "AutoUpdater", StringComparison.OrdinalIgnoreCase)
        );

        if (selfApp != null && localVersion != selfApp.version)
        {
            Console.WriteLine($"检测到 AutoUpdater 新版本 {selfApp.version}，准备自我更新...");

            // 清空 Temp
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            // 下载 ZIP
            string zipPath = Path.Combine(tempDir, selfApp.fileName);
            await DownloadFileAsync(selfApp.url, zipPath, "AutoUpdater");

            // 解压到 Temp
            ZipFile.ExtractToDirectory(zipPath, tempDir, true);

            // 启动临时实例替换文件，传递版本号
            string tempExe = Path.Combine(tempDir, "AutoUpdater.exe");
            if (!File.Exists(tempExe))
            {
                Console.WriteLine("自我更新失败：解压后找不到 AutoUpdater.exe");
                return;
            }

            Process.Start(
                new ProcessStartInfo(tempExe, $"--replace {selfApp.version}")
                {
                    UseShellExecute = true,
                }
            );
            return; // 当前实例退出
        }

        // --- 更新其他应用 ---
        foreach (var app in updateList.apps)
        {
            if (app.name == "AutoUpdater")
                continue;

            string targetAppDir = Path.Combine(appDir, app.name);
            Directory.CreateDirectory(targetAppDir);
            string appExePath = Path.Combine(targetAppDir, app.exeName);

            string localAppVersionFile = Path.Combine(targetAppDir, "local_version.txt");
            string localAppVersion = File.Exists(localAppVersionFile)
                ? File.ReadAllText(localAppVersionFile)
                : "0.0.0.0";

            if (localAppVersion != app.version)
            {
                Console.WriteLine($"检测到 {app.name} 新版本 {app.version}，正在更新...");

                EnsureAppNotRunning(app.exeName);

                // 清空 Temp
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                // 下载 ZIP
                string zipPath = Path.Combine(tempDir, app.fileName);
                await DownloadFileAsync(app.url, zipPath, app.name);

                // 解压到 Temp
                ZipFile.ExtractToDirectory(zipPath, tempDir, true);

                // 替换目标目录文件
                foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(tempDir, file);
                    string dest = Path.Combine(targetAppDir, relative);

                    string destDir = Path.GetDirectoryName(dest)!;
                    if (!Directory.Exists(destDir))
                        Directory.CreateDirectory(destDir);

                    File.Copy(file, dest, true);
                }

                File.WriteAllText(localAppVersionFile, app.version);
                Console.WriteLine($"{app.name} 更新完成！");
            }

            // 启动应用
            if (File.Exists(appExePath))
            {
                Console.WriteLine($"启动 {app.name}...");
                Process.Start(new ProcessStartInfo(appExePath) { UseShellExecute = true });
            }
        }

        Console.WriteLine("\n更新检查完成。按任意键退出...");
        Console.ReadKey();
    }

    private static bool EnsureAppNotRunning(string exeName)
    {
        var processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exeName));
        if (processes.Length == 0)
            return true;

        Console.WriteLine($"{exeName} 正在运行，尝试关闭...");
        foreach (var proc in processes)
        {
            try
            {
                proc.Kill();
                proc.WaitForExit();
            }
            catch
            {
                Console.WriteLine($"无法关闭进程 {proc.Id}");
                return false;
            }
        }
        return true;
    }

    private static async Task<string?> ResolveUpdateServerUrl()
    {
        string appDir = AppContext.BaseDirectory;
        string configPath = Path.Combine(appDir, "config.json");
        string? updateServerUrl = null;

        if (File.Exists(configPath))
        {
            try
            {
                var cfg = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(configPath)
                );
                if (cfg != null && cfg.TryGetValue("updateServerUrl", out string? localUrl))
                {
                    updateServerUrl = localUrl;
                    Console.WriteLine("从本地 config.json 获取服务器地址。");
                }
            }
            catch { }
        }

        if (updateServerUrl == null)
        {
            try
            {
                using var response = await http.GetAsync(DefaultRootConfigUrl);
                response.EnsureSuccessStatusCode(); // 如果返回非 2xx 会抛异常
                string json = await response.Content.ReadAsStringAsync();


                var updateList = JsonSerializer.Deserialize<UpdateList>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (updateList != null)
                {
                    var selfApp = updateList.apps.Find(a =>
                        string.Equals(a.name, "AutoUpdater", StringComparison.OrdinalIgnoreCase)
                    );

                    if (selfApp != null)
                    {
                        updateServerUrl = selfApp.url;
                        Console.WriteLine("成功从远程 updater.json 获取 AutoUpdater 地址。");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取远程 updater.json 失败: {ex.Message}");
            }
        }

        if (updateServerUrl == null)
            updateServerUrl = "http://localhost:5010/update.json";
        return updateServerUrl;
    }

    private static async Task DownloadFileAsync(string url, string destPath, string appName)
    {
        Console.WriteLine($"[{appName}] 下载中: {url}");
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
        await stream.CopyToAsync(fs);
        Console.WriteLine($"[{appName}] 下载完成: {destPath}");
    }

    private class UpdateList
    {
        public List<UpdateInfo> apps { get; set; } = new();
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
