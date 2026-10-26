using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Mcitem.EasiPlugin.MetaDataEditer;

/// <summary>安装后提示。运行于独立进程，只依赖纯 WPF，不使用宿主资源。</summary>
public partial class InstallWizardWindow : Window
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _cancellationToken;

    public InstallWizardWindow()
    {
        _cancellationToken = _lifetime.Token;
        InitializeComponent();
        DataContext = new ViewModel(new InstallEnvironmentInfo(), loading: true);
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            try { _lifetime.Cancel(); }
            catch (Exception exception) { PluginLog.Exception("取消环境检测失败", exception); }
            finally { _lifetime.Dispose(); }
        };
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            Loaded -= OnLoaded;
            var environment = await Task.Run(() => InstallEnvironmentInfo.Detect(_cancellationToken), _cancellationToken);
            _cancellationToken.ThrowIfCancellationRequested();
            DataContext = new ViewModel(environment);
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { PluginError.Report("检测安装环境失败", exception, this); }
    }

    private void OnPrimaryClick(object sender, RoutedEventArgs e) => SetResult(true);
    private void OnCancelClick(object sender, RoutedEventArgs e) => SetResult(false);

    private void SetResult(bool result)
    {
        try { DialogResult = result; }
        catch (Exception exception) { PluginError.Report("关闭安装提示失败", exception, this); }
    }

    private sealed class ViewModel
    {
        public ViewModel(InstallEnvironmentInfo environment, bool loading = false)
        {
            var assembly = Assembly.GetExecutingAssembly();
            ProductNameText = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "元素元数据编辑器";
            VersionText = "版本：" + (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString());
            AuthorText = "开发者：" + assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
            DescriptionText = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? string.Empty;
            HostStatusText = loading ? "正在检测宿主环境…" : environment.IsHostRunning
                ? $"检测到 {environment.HostProcessCount} 个希沃白板进程，请保存课件后完全退出并重新启动。"
                : "希沃白板未在运行，启动后即可使用插件。";
            HostPathText = loading ? "正在读取安装路径…" : environment.HostDirectory ?? "未检测到安装目录，请确认已安装希沃白板。";
            var steps = new List<string>();
            if (environment.IsHostRunning)
            {
                steps.Add("保存当前课件，完全退出希沃白板（包括托盘中的进程）。");
            }
            steps.Add("启动希沃白板，进入备课模式并打开课件。");
            steps.Add("选中元素，右键选择“编辑元素元数据”。");
            steps.Add("保存元数据更改后，再保存课件。");
            StepItems = steps;
        }

        public string TitleText => "安装完成";
        public string SubtitleText => "插件已写入希沃白板插件目录。";
        public string ProductNameText { get; }
        public string VersionText { get; }
        public string AuthorText { get; }
        public string DescriptionText { get; }
        public string HostCheckTitle => "宿主环境";
        public string HostStatusLabel => "运行状态";
        public string HostStatusText { get; }
        public string HostPathLabel => "安装路径";
        public string HostPathText { get; }
        public string StepsTitle => "开始使用";
        public IReadOnlyList<string> StepItems { get; }
        public string PrimaryButtonText => "完成";
        public string CancelText => "关闭";
    }
}
