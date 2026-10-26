using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Mcitem.EasiPlugin.MetaDataEditer;

/// <summary>WPF 同步命令边界：观察异步任务、阻止重复执行，并隔离插件异常。</summary>
internal sealed class PluginCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly CancellationToken _cancellationToken;
    private bool _running;

    public PluginCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null,
        CancellationToken cancellationToken = default)
    {
        _execute = execute;
        _canExecute = canExecute;
        _cancellationToken = cancellationToken;
    }

    public bool CanExecute(object? parameter)
    {
        try { return !_running && !_cancellationToken.IsCancellationRequested && (_canExecute?.Invoke() ?? true); }
        catch (Exception exception) { PluginLog.Exception("检查命令状态失败", exception); return false; }
    }

    public async void Execute(object? parameter)
    {
        // ICommand 必须返回 void；所有 await 和 finally 均包含在异常边界中。
        try
        {
            if (!CanExecute(parameter)) return;
            _running = true;
            try
            {
                CommandManager.InvalidateRequerySuggested();
                await _execute(_cancellationToken);
            }
            finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { PluginError.Report("执行插件命令失败", exception); }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

internal static class PluginError
{
    internal static void Report(string context, Exception exception, Window? owner = null)
    {
        PluginLog.Exception(context, exception);
        try
        {
            var dispatcher = owner?.Dispatcher ?? Application.Current?.Dispatcher;
            if (dispatcher is null || !dispatcher.CheckAccess() || dispatcher.HasShutdownStarted) return;
            var message = context + "，请查看插件日志。";
            if (owner is { IsVisible: true })
                MessageBox.Show(owner, message, "元素元数据", MessageBoxButton.OK, MessageBoxImage.Error);
            else
                MessageBox.Show(message, "元素元数据", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception displayException) { PluginLog.Exception("显示插件错误提示失败", displayException); }
    }
}
