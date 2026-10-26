using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Mcitem.EasiPlugin.MetaDataEditer;

internal sealed class InstallEnvironmentInfo
{
    public bool IsHostRunning { get; private set; }
    public int HostProcessCount { get; private set; }
    public string? HostDirectory { get; private set; }

    public static InstallEnvironmentInfo Detect(CancellationToken cancellationToken)
    {
        var info = new InstallEnvironmentInfo();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = Process.GetProcessesByName("EasiNote");
            try
            {
                info.HostProcessCount = processes.Length;
                info.IsHostRunning = processes.Length > 0;
                foreach (var process in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        info.HostDirectory = Path.GetDirectoryName(process.MainModule?.FileName);
                        if (info.HostDirectory is not null) break;
                    }
                    catch (Exception exception)
                    {
                        PluginLog.Warn($"读取宿主路径失败：{exception.Message}");
                    }
                }
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }

            if (info.HostDirectory is null)
            {
                var roots = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Seewo", "EasiNote5"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Seewo", "EasiNote5"),
                };
                info.HostDirectory = roots.Where(Directory.Exists)
                    .SelectMany(root => Directory.EnumerateDirectories(root, "EasiNote5_*"))
                    .Select(path => { cancellationToken.ThrowIfCancellationRequested(); return path; })
                    .OrderByDescending(path => Version.TryParse(Path.GetFileName(path)["EasiNote5_".Length..], out var version) ? version : new Version())
                    .Select(path => Path.Combine(path, "Main"))
                    .FirstOrDefault(path => File.Exists(Path.Combine(path, "EasiNote.exe")));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            PluginLog.Exception("检测宿主环境失败", exception);
        }
        return info;
    }
}
