using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Cvte.Composition;
using Cvte.EasiNote;
using Cvte.Windows.Localization;

namespace Mcitem.EasiPlugin.MetaDataEditer;

internal sealed class Program : dotnetCampus.EasiPlugins.EasiPlugin
{
    protected override async Task OnRunningAsync()
    {
        using var lifetime = new CancellationTokenSource();
        Dispatcher? dispatcher = null;
        EventHandler onShutdown = (_, _) =>
        {
            try { lifetime.Cancel(); }
            catch (Exception exception) { PluginLog.Exception("取消插件初始化失败", exception); }
        };
        try
        {
            PluginLog.Info("插件开始初始化");
            if (EN.CommandOptions.IsCloud) return;
            dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;
            dispatcher.ShutdownStarted += onShutdown;
            var token = lifetime.Token;
            var manager = await Container.Current.GetAsync<IUIItemManager>().WaitAsync(token).ConfigureAwait(false);
            await dispatcher.InvokeAsync(() =>
            {
                // 两种菜单均继承 BoardEditMenuItem 的语言键规则。
                // 基类在构造时根据类型名去掉 MenuItem 生成 Key；先注册语言，不再覆盖 Key。
                var translations = new Dictionary<string, string>
                {
                    [MenuLanguageKey(typeof(ElementMetadataMenuItem))] = ElementMetadataMenu.Caption,
                    [MenuLanguageKey(typeof(BoardMetadataMenuItem))] = ElementMetadataMenu.Caption,
                };
                Lang.Sources.Add(new DictionaryLanguageSource
                {
                    [CultureInfo.GetCultureInfo("zh-CN")] = translations,
                    [CultureInfo.GetCultureInfo("zh-CHS")] = translations,
                    [CultureInfo.GetCultureInfo("en-US")] = new Dictionary<string, string>
                    {
                        [MenuLanguageKey(typeof(ElementMetadataMenuItem))] = "Edit element metadata",
                        [MenuLanguageKey(typeof(BoardMetadataMenuItem))] = "Edit element metadata",
                    },
                });
                manager.Append(_ => new ElementMetadataMenuItem(), new UIItemAttribute(UIItemPurposes.ElementEditMenu));
                manager.Append(_ => new BoardMetadataMenuItem(), new UIItemAttribute(UIItemPurposes.BoardEditMenu));
                PluginLog.Info("元素元数据菜单注册完成");
            }, DispatcherPriority.Normal, token);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            PluginLog.Exception("初始化元素元数据菜单失败", exception);
        }
        finally
        {
            if (dispatcher is not null) dispatcher.ShutdownStarted -= onShutdown;
        }
    }

    private static string MenuLanguageKey(Type type) =>
        "Lang.BoardEditContextMenu." + type.Name.Replace("MenuItem", string.Empty);
}
