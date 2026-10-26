using System;
using System.Linq;

namespace Mcitem.EasiPlugin.MetaDataEditer;

/// <summary>独立安装回调；宿主插件入口仍由 SDK 生成的 AssemblyProvider 提供。</summary>
internal static class InstallEntryPoint
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (!args.Any(argument => string.Equals(argument, "postinstall", StringComparison.OrdinalIgnoreCase)))
            return 0;
        try
        {
            // 已经安装完成，关闭提示窗口不应把成功的安装变成失败。
            new InstallWizardWindow().ShowDialog();
        }
        catch (Exception exception)
        {
            PluginLog.Exception("显示安装完成提示失败", exception);
        }
        return 0;
    }
}
