using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Mcitem.EasiPlugin.MetaDataEditer;

/// <summary>UI 线程只向有界队列投递；后台任务负责文件 I/O，所有异常在任务内部观察。</summary>
internal static class PluginLog
{
    private static readonly Channel<string> Queue = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    // 持有任务，避免无主的后台操作；WriteLoopAsync 内部捕获包括释放资源在内的异常。
    private static readonly Task WriterTask = Task.Run(() => WriteLoopAsync(CancellationToken.None));

    static PluginLog()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Queue.Writer.TryComplete(); }
            catch (Exception exception) { SafeTrace(exception.GetType().Name); }
        };
    }

    internal static string LogFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Seewo", "EasiNote5", "Plugins", "EasiPlugin.MetaDataEditer", "Logs",
        $"EasiPlugin.MetaDataEditer.{Environment.ProcessId}.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    public static void Exception(string context, Exception exception)
    {
        // 不记录异常消息、进程命令行和字段值，以免带入课件内容或凭据。
        Write("ERROR", $"{context}: {exception.GetType().FullName}; {exception.StackTrace}");
    }

    private static void Write(string level, string message)
    {
        try { Queue.Writer.TryWrite($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [P{Environment.ProcessId}] [{level}] {message}"); }
        catch (Exception exception) { SafeTrace(exception.GetType().Name); }
    }

    private static async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
            await using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete, 4096, useAsync: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            await foreach (var line in Queue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
                SafeTrace(line);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { SafeTrace("插件日志写入失败：" + exception.GetType().Name); }
    }

    private static void SafeTrace(string message)
    {
        try { Trace.WriteLine(message); }
        catch (Exception) { /* 最后一级日志出口失败时，不得再向宿主抛异常。 */ }
    }
}
