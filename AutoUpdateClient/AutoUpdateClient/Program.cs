using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

class AutoUpdateClient
{
    private static readonly string UpdateServerUrl = "http://localhost:5010/update.json";
    private static readonly string DownloadFolder = Path.Combine(
        AppContext.BaseDirectory,
        "Downloads"
    );
    private static readonly string InstallRootDir = @"D:\Apps";

    private static readonly string UpdaterInstallDir = Path.Combine(
        InstallRootDir,
        "AutoUpdateClient"
    );
    private static readonly string ClientInstallDir = Path.Combine(
        UpdaterInstallDir,
        "AutoUpdateClient"
    );
    private static readonly string TempClientInstallDir = Path.Combine(UpdaterInstallDir, "Temp");

    //private static readonly string LocalVersionFile = Path.Combine(
    //    ClientInstallDir,
    //    "client_version.txt"
    //);
    private static readonly HttpClient http = new();

    static async Task Main()
    {
        Directory.CreateDirectory(DownloadFolder);
        Directory.CreateDirectory(InstallRootDir);

        string exeDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        // 判断是否已在目标安装目录
        if (!exeDir.Equals(ClientInstallDir, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("首次运行或不在目标目录，开始安装 AutoUpdateClient...");

            Directory.CreateDirectory(ClientInstallDir);

            // 下载 AutoUpdateClient ZIP
            var json = await http.GetStringAsync(UpdateServerUrl);
            var updateList = JsonSerializer.Deserialize<UpdateList>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            )!;

            var clientApp = updateList.apps.Find(a =>
                a.name.Equals("AutoUpdateClient", StringComparison.OrdinalIgnoreCase)
            );
            var updaterApp = updateList.apps.Find(a =>
                a.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase)
            );

            if (clientApp == null || updaterApp == null)
            {
                Console.WriteLine("未找到 AutoUpdateClient 或 AutoUpdater 信息，无法安装！");
                return;
            }

            // 下载并解压 AutoUpdateClient
            string clientZip = Path.Combine(DownloadFolder, clientApp.fileName);
            await DownloadFileAsync(clientApp.url, clientZip, "AutoUpdateClient");
            ZipFile.ExtractToDirectory(clientZip, TempClientInstallDir, true);

            // 下载并解压 AutoUpdater
            string updaterZip = Path.Combine(DownloadFolder, updaterApp.fileName);
            await DownloadFileAsync(updaterApp.url, updaterZip, "AutoUpdater");
            ZipFile.ExtractToDirectory(updaterZip, UpdaterInstallDir, true);

            Console.WriteLine("安装完成，启动 AutoUpdater 更新自身和其他软件...");
            string updaterExe = Path.Combine(UpdaterInstallDir, "AutoUpdater.exe");
            if (File.Exists(updaterExe))
            {
                Process.Start(new ProcessStartInfo(updaterExe) { UseShellExecute = true });
            }

            return; // 安装完成退出
        }

        Console.WriteLine("已在目标安装目录，开始检查其他软件更新...");

        // --- 下载每个软件及其 AutoUpdater ---
        var serverJson = await http.GetStringAsync(UpdateServerUrl);
        var updateList2 = JsonSerializer.Deserialize<UpdateList>(
            serverJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        )!;

        var updaterInfo = updateList2.apps.Find(a =>
            a.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase)
        );
        if (updaterInfo == null)
        {
            Console.WriteLine("未找到 AutoUpdater 信息！");
            return;
        }

        foreach (var app in updateList2.apps)
        {
            if (
                app.name.Equals("AutoUpdater", StringComparison.OrdinalIgnoreCase)
                || app.name.Equals("AutoUpdateClient", StringComparison.OrdinalIgnoreCase)
            )
                continue;

            string appDir = Path.Combine(InstallRootDir, app.name);
            string updaterExe = Path.Combine(appDir, "AutoUpdater.exe");

            if (!Directory.Exists(appDir) || !File.Exists(updaterExe))
            {
                Console.WriteLine($"\n[{app.name}] 未安装，是否下载? (Y/N)");
                if (Console.ReadKey().Key == ConsoleKey.Y)
                {
                    await DownloadSoftwareWithUpdater(app, appDir, updaterInfo);
                    Console.WriteLine(
                        $"\n[{app.name}] 下载完成！请使用 {updaterExe} 启动软件并自动更新。"
                    );
                }
                else
                {
                    Console.WriteLine($"\n[{app.name}] 跳过下载。");
                }
            }
        }

        Console.WriteLine("\n首次安装/检查完成。按任意键退出...");
        Console.ReadKey();
    }

    private static async Task DownloadSoftwareWithUpdater(
        UpdateInfo app,
        string appDir,
        UpdateInfo updaterApp
    )
    {
        Directory.CreateDirectory(appDir);

        // 下载软件 ZIP
        string zipPath = Path.Combine(DownloadFolder, app.fileName);
        await DownloadFileAsync(app.url, zipPath, app.name);

        // 解压到软件目录
        ZipFile.ExtractToDirectory(zipPath, appDir, overwriteFiles: true);

        // 下载 AutoUpdater ZIP
        string updaterZip = Path.Combine(DownloadFolder, updaterApp.fileName);
        await DownloadFileAsync(updaterApp.url, updaterZip, "AutoUpdater");

        // 解压 AutoUpdater 到软件目录
        ZipFile.ExtractToDirectory(updaterZip, appDir, overwriteFiles: true);

        Console.WriteLine($"[{app.name}] 软件和 AutoUpdater 已安装完成。");
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
