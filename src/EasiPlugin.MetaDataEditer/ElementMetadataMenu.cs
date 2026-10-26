using System;
using System.Threading.Tasks;
using System.Windows;
using Cvte.EasiNote;
using Cvte.Paint;
using Cvte.Paint.Framework;

namespace Mcitem.EasiPlugin.MetaDataEditer;

public sealed class ElementMetadataMenuItem : ElementEditMenuItem
{
    public ElementMetadataMenuItem()
    {
        SortHint = 60;
        Text = ElementMetadataMenu.Caption;
        Predicate = elements => elements.Count > 0;
        Command = new PluginCommand(_ => { ElementMetadataMenu.Open(Board); return Task.CompletedTask; });
    }
}

public sealed class BoardMetadataMenuItem : BoardEditMenuItem
{
    public BoardMetadataMenuItem()
    {
        SortHint = 60;
        Text = ElementMetadataMenu.Caption;
        Predicate = elements => elements.Count > 0;
        Command = new PluginCommand(_ => { ElementMetadataMenu.Open(Board); return Task.CompletedTask; });
    }
}

internal static class ElementMetadataMenu
{
    internal const string Caption = "编辑元素元数据";

    internal static void Open(Board? board)
    {
        if (board is null) return;
        Window? owner = null;
        try
        {
            owner = Window.GetWindow(board);
            var elements = BoardService.GetBoardService(board)?.GetSelectedElements();
            if (elements is null || elements.Length == 0)
            {
                Message(owner, "请先选中一个元素。", MessageBoxImage.Information);
                return;
            }
            var window = new ElementMetadataWindow(elements[0], elements.Length) { Owner = owner };
            window.ShowDialog();
        }
        catch (Exception exception)
        {
            PluginError.Report("打开元素元数据失败", exception, owner);
        }
    }

    private static void Message(Window? owner, string message, MessageBoxImage image)
    {
        if (owner is null) MessageBox.Show(message, "元素元数据", MessageBoxButton.OK, image);
        else MessageBox.Show(owner, message, "元素元数据", MessageBoxButton.OK, image);
    }
}
