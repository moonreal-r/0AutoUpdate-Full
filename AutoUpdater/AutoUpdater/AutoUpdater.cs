using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

class AutoUpdater
{
    private static readonly HttpClient http = new();
    private static string UpdateServerUrl = "http://127.0.0.1:5010/update.json";

    static async Task Main()
    {
        string appDir = AppContext.BaseDirectory;
        string configPath = Path.Combine(appDir, "config.json");

        if (File.Exists(configPath))
        {
            try
            {
                var configJson = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
                if (
                    config != null
                    && config.TryGetValue("UpdateServerUrl", out string? url)
                    && !string.IsNullOrWhiteSpace(url)
                )
                {
                    UpdateServerUrl = url;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取配置文件失败，使用默认更新地址: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("未找到 config.json，使用默认更新地址。");
        }

        Console.WriteLine($"当前运行目录: {AppContext.BaseDirectory}");

        string exePath = Path.Combine(appDir, "AutoUpdater.exe");
        string localVersionFile = Path.Combine(appDir, "local_version.txt");
        string AutoUpdaterTempDir = Path.Combine(appDir, "AutoUpdaterTemp");
        string tempDir = Path.Combine(appDir, "Temp");

        // === 1. 拉取 update.json ===
        Console.WriteLine($"正在连接更新服务器: {UpdateServerUrl}");
        string json;
        try
        {
            json = await http.GetStringAsync(UpdateServerUrl);
            Console.WriteLine("获取 update.json 成功");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"获取 update.json 失败: {ex.Message}");
            return;
        }

        var updateList = JsonSerializer.Deserialize<UpdateList>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        )!;

        // --- 获取 AutoUpdater 自身信息 ---
        var selfApp = updateList.apps.FirstOrDefault(a =>
            a.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase)
        );
        if (selfApp == null)
        {
            Console.WriteLine("update.json 中没有找到 AutoUpdater 项，退出。");
            return;
        }

        string localVersion = File.Exists(localVersionFile)
            ? File.ReadAllText(localVersionFile).Trim()
            : "0.0.0.0";

        // === 2. 自我更新（如果需要） ===
        if (localVersion != selfApp.version)
        {
            Console.WriteLine($"检测到 AutoUpdater 新版本 {selfApp.version}，开始自我更新...");

            if (Directory.Exists(AutoUpdaterTempDir))
                Directory.Delete(AutoUpdaterTempDir, true);
            Directory.CreateDirectory(AutoUpdaterTempDir);

            string zipPath = Path.Combine(AutoUpdaterTempDir, selfApp.fileName);
            Console.WriteLine($"当前 自我更新 zipPath: {zipPath}");

            await DownloadFileAsync(selfApp.url, zipPath, "AutoUpdater");

            ZipFile.ExtractToDirectory(zipPath, AutoUpdaterTempDir, true);
            Console.WriteLine($"当前 自我更新 解压路径 AutoUpdaterTempDir: {AutoUpdaterTempDir}");

            string newExe = Path.Combine(AutoUpdaterTempDir, "AutoUpdater.exe");
            if (!File.Exists(newExe))
            {
                Console.WriteLine("自我更新失败：未找到新 AutoUpdater.exe");
                return;
            }

            string replacerPath = Path.Combine(appDir, "UpdaterReplacer.exe");
            if (!File.Exists(replacerPath))
            {
                Console.WriteLine("缺少 UpdaterReplacer.exe");
                return;
            }

            Console.WriteLine($"自我更新验证:{replacerPath} {exePath} {selfApp.version}");

            Process.Start(
                new ProcessStartInfo
                {
                    FileName = replacerPath,
                    Arguments = $"\"{exePath}\" \"{selfApp.version}\"",
                    UseShellExecute = true,
                }
            );

            Console.WriteLine("UpdaterReplacer 已启动");

            return; // 当前 AutoUpdater 退出
        }

        Console.WriteLine("AutoUpdater 已是最新版本。");

        // === 3. 更新主应用程序 ===
        UpdateInfo? mainApp = null;

        foreach (var app in updateList.apps)
        {
            if (app.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase))
                continue; // 跳过自身

            string appFolder = Path.Combine(appDir, app.name);
            string exePathInFolder = Path.Combine(appFolder, app.exeName);

            if (Directory.Exists(appFolder) && File.Exists(exePathInFolder))
            {
                mainApp = app;
                break; // 找到第一个符合条件的主应用
            }
        }

        if (mainApp == null)
        {
            Console.WriteLine("未找到主应用程序目录或 exe，程序退出。");
            return;
        }

        string mainAppDir = Path.Combine(appDir, mainApp.name);
        string mainExePath = Path.Combine(mainAppDir, mainApp.exeName);
        string mainVersionFile = Path.Combine(mainAppDir, "local_version.txt");

        Console.WriteLine($"主应用程序: {mainApp.name} ({mainExePath})");

        string localMainVersion = File.Exists(mainVersionFile)
            ? File.ReadAllText(mainVersionFile).Trim()
            : "0.0.0.0";

        if (localMainVersion != mainApp.version)
        {
            Console.WriteLine($"检测到 {mainApp.name} 新版本 {mainApp.version}，开始更新...");

            EnsureAppNotRunning(mainApp.exeName);

            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
            Directory.CreateDirectory(tempDir);

            string zipPath = Path.Combine(tempDir, mainApp.fileName);
            Console.WriteLine($"当前 主应用更新 zipPath: {zipPath}");

            await DownloadFileAsync(mainApp.url, zipPath, mainApp.name);

            ZipFile.ExtractToDirectory(zipPath, tempDir, true);

            Directory.CreateDirectory(mainAppDir);
            foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(tempDir, file);
                string dest = Path.Combine(mainAppDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }

            File.WriteAllText(mainVersionFile, mainApp.version);
            Console.WriteLine($"{mainApp.name} 更新完成！");

            try
            {
                Directory.Delete(tempDir, true);
                Console.WriteLine("已清理 Temp 目录");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清理 Temp 目录失败: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine($"{mainApp.name} 已是最新版本。");
        }

        // === 4. 启动主应用 ===
        if (File.Exists(mainExePath))
        {
            Console.WriteLine($"启动主应用 {mainApp.name}...");
            Process.Start(new ProcessStartInfo(mainExePath) { UseShellExecute = true });
        }

        Console.WriteLine("\n更新完成");
    }

    private static async Task DownloadFileAsync(string url, string destPath, string appName)
    {
        try
        {
            Console.WriteLine($"[{appName}] 下载中: {url}");
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fs = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            await stream.CopyToAsync(fs);
            Console.WriteLine($"[{appName}] 下载完成: {destPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"DownloadFileAsync 出错:{ex}");
        }
    }

    private static void EnsureAppNotRunning(string exeName, int waitMilliseconds = 5000)
    {
        string processName = Path.GetFileNameWithoutExtension(exeName);
        var processes = Process.GetProcessesByName(processName);

        foreach (var proc in processes)
        {
            try
            {
                // 尝试正常关闭
                if (!proc.HasExited && proc.MainWindowHandle != IntPtr.Zero)
                {
                    Console.WriteLine($"尝试正常关闭 {exeName}...");
                    proc.CloseMainWindow(); // 发送 WM_CLOSE 消息
                    if (!proc.WaitForExit(waitMilliseconds))
                    {
                        // 超时未退出，使用强制终止
                        Console.WriteLine($"{exeName} 未响应，强制终止中...");
                        proc.Kill();
                        proc.WaitForExit();
                    }
                    else
                    {
                        Console.WriteLine($"{exeName} 已正常关闭。");
                    }
                }
                else
                {
                    // 无窗口或已经退出，直接 Kill
                    proc.Kill();
                    proc.WaitForExit();
                    Console.WriteLine($"已强制关闭 {exeName}（无窗口或已退出）。");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭 {exeName} 时发生异常: {ex.Message}");
            }
        }

        if (!processes.Any())
            Console.WriteLine($"{exeName} 未在运行中。");
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
