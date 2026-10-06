using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace MusicRpc;

internal static class BetterNcmBootstrap
{
    public static void LaunchInstalledCopy(string stateFile)
    {
        var source = Environment.ProcessPath ?? throw new InvalidOperationException("无法找到程序路径。");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "yySync", "plugin-helper");
        Directory.CreateDirectory(directory);

        using var sourceStream = File.OpenRead(source);
        var hash = Convert.ToHexString(SHA256.HashData(sourceStream));
        var destination = Path.Combine(directory, $"yySync-{hash[..16]}.exe");
        if (!File.Exists(destination))
            File.Copy(source, destination);

        var startInfo = new ProcessStartInfo(destination)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--betterncm-installed");
        startInfo.ArgumentList.Add(stateFile);
        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException("Windows 未能启动同步组件。");
    }
}
