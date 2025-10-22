using System.Diagnostics;

class UpdaterReplacer
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("用法: UpdaterReplacer.exe <目标程序exe路径> <新版本号>");
            return;
        }

        // 目标 exe 的完整路径
        string targetExePath = args[0];
        string newVersion = args[1];

        if (!File.Exists(targetExePath))
        {
            Console.WriteLine($"目标程序不存在: {targetExePath}");
            return;
        }

        // 主程序所在目录
        string appDir = Path.GetDirectoryName(targetExePath)!;
        string tempDir = Path.Combine(appDir, "AutoUpdaterTemp");
        string versionFile = Path.Combine(appDir, "local_version.txt");

        Console.WriteLine($"=== 启动 UpdaterReplacer ===");
        Console.WriteLine($"目标程序: {targetExePath}");
        Console.WriteLine($"新版本号: {newVersion}");
        Console.WriteLine($"主程序目录: {appDir}");
        Console.WriteLine();

        // === 1. 等待旧程序退出 ===
        string processName = Path.GetFileNameWithoutExtension(targetExePath);
        WaitForProcessExit(processName);

        // === 2. 检查临时更新目录 ===
        if (!Directory.Exists(tempDir))
        {
            Console.WriteLine($"错误: 找不到临时目录 {tempDir}");
            return;
        }

        // === 3. 替换文件到主程序目录 ===
        Console.WriteLine("开始替换文件...");
        foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(tempDir, file);
            string dest = Path.Combine(appDir, relative);

            string destDir = Path.GetDirectoryName(dest)!;
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            try
            {
                File.Copy(file, dest, true);
                Console.WriteLine($"替换: {relative}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"替换失败 {relative}: {ex.Message}");
            }
        }

        // === 4. 写入新版本号 ===
        File.WriteAllText(versionFile, newVersion);
        Console.WriteLine($"版本号已更新 -> {newVersion}");

        // === 5. 清理临时文件 ===
        try
        {
            Directory.Delete(tempDir, true);
            Console.WriteLine("已清理 Temp 目录");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"清理 Temp 目录失败: {ex.Message}");
        }

        // === 6. 启动新版本主程序 ===
        try
        {
            Console.WriteLine("启动新版本主程序...");
            Process.Start(
                new ProcessStartInfo(targetExePath)
                {
                    UseShellExecute = true,
                    WorkingDirectory = appDir,
                }
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"启动新版本失败: {ex.Message}");
        }

        Console.WriteLine("更新完成，退出 UpdaterReplacer。");
    }

    /// <summary>
    /// 等待指定程序完全退出
    /// </summary>
    private static void WaitForProcessExit(string processName)
    {
        Console.WriteLine($"等待 {processName} 退出中...");

        int retries = 0;
        while (true)
        {
            var procs = Process.GetProcessesByName(processName);
            if (procs.Length == 0)
                break;

            foreach (var p in procs)
            {
                try
                {
                    Console.WriteLine($"进程 {p.Id} 仍在运行，等待中...");
                    p.WaitForExit(1000);
                }
                catch { }
            }

            Thread.Sleep(500);
            retries++;

            if (retries > 30) // 最多等 30 秒
            {
                Console.WriteLine("仍有旧程序未退出，强制结束。");
                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill();
                    }
                    catch { }
                }
                break;
            }
        }

        Console.WriteLine($"{processName} 已退出。");
    }
}
