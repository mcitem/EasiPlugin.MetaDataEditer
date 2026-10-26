using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Cvte.Paint.Framework;

namespace Mcitem.EasiPlugin.MetaDataEditer;

internal static class SaveInfoFieldExplorer
{
    private const int MaxDepth = 4;
    private const int MaxFields = 5000;

    public static async Task<IReadOnlyList<SaveInfoField>> ExploreAsync(ElementSaveInfo saveInfo,
        Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        // 属性 getter 可能访问 WPF 对象，始终在宿主 UI 线程读取，按时间片让出调度。
        dispatcher.VerifyAccess();
        var fields = new List<SaveInfoField>();
        var ancestors = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var slice = Stopwatch.StartNew();

        async Task YieldAsync()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (slice.ElapsedMilliseconds < 8) return;
            await dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken);
            slice.Restart();
        }

        async Task ExploreAsync(object owner, string prefix, int depth, string? inheritedReadOnly = null)
        {
            await YieldAsync();
            if (depth > MaxDepth || fields.Count >= MaxFields || !ancestors.Add(owner)) return;
            try
            {
                foreach (var property in owner.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    await YieldAsync();
                    if (fields.Count >= MaxFields) break;
                    if (property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true
                        || property.GetCustomAttribute<BrowsableAttribute>()?.Browsable == false) continue;
                    var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
                    var branchReadOnly = inheritedReadOnly
                        ?? (property.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true ? "此属性分支被宿主标记为只读。" : null);
                    object? value;
                    try { value = property.GetValue(owner); }
                    catch (Exception exception)
                    {
                        PluginLog.Exception($"读取字段 {path} 失败", exception);
                        fields.Add(new SaveInfoField(path, property.PropertyType, "（读取失败）", owner, property, "宿主未能读取此属性。"));
                        continue;
                    }

                    if (IsScalar(property.PropertyType))
                        fields.Add(new SaveInfoField(path, property.PropertyType, value, owner, property, branchReadOnly ?? ReadOnlyReason(property)));
                    else if (value is IEnumerable collection and not string)
                    {
                        var index = 0;
                        foreach (var item in collection)
                        {
                            await YieldAsync();
                            if (fields.Count >= MaxFields) break;
                            if (index >= 500)
                            {
                                fields.Add(new SaveInfoField(path + "[…]", typeof(string), "仅显示前 500 项", owner,
                                    readOnlyReason: "集合过大，已限制展开数量。请使用宿主对应工具查看完整内容。"));
                                break;
                            }
                            var itemPath = $"{path}[{index++}]";
                            if (item is not null && !IsScalar(item.GetType()) && CanDescend(item.GetType()) && depth < MaxDepth)
                                await ExploreAsync(item, itemPath, depth + 1, branchReadOnly);
                            else
                                fields.Add(new SaveInfoField(itemPath, item?.GetType() ?? typeof(object), item, owner,
                                    readOnlyReason: "集合项仅供查看，请使用宿主工具修改集合。"));
                        }
                    }
                    else if (value is not null && CanDescend(value.GetType()) && depth < MaxDepth)
                        await ExploreAsync(value, path, depth + 1, branchReadOnly);
                    else
                        fields.Add(new SaveInfoField(path, property.PropertyType, value, owner, property,
                            branchReadOnly ?? ReadOnlyReason(property) ?? "复杂属性仅供查看，请使用宿主对应的编辑工具。"));
                }
            }
            finally { ancestors.Remove(owner); }
        }

        await ExploreAsync(saveInfo, string.Empty, 0);
        if (fields.Count >= MaxFields)
            fields.Add(new SaveInfoField("…", typeof(string), "已达到属性数量上限", saveInfo,
                readOnlyReason: "最多展开 5000 项属性，请使用宿主工具编辑更复杂的元素。"));
        // 排序只读托管字段描述，不访问宿主对象。
        return await Task.Run<IReadOnlyList<SaveInfoField>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return fields.OrderBy(item => item.Category).ThenBy(item => item.Path, StringComparer.Ordinal).ToArray();
        }, cancellationToken);
    }

    private static string? ReadOnlyReason(PropertyInfo property)
    {
        if (property.GetCustomAttribute<ReadOnlyAttribute>()?.IsReadOnly == true) return "此属性被宿主标记为只读。";
        if (property.SetMethod?.IsPublic != true) return "此属性没有公开的设置方法。";
        if (property.SetMethod.ReturnParameter.GetRequiredCustomModifiers()
            .Any(type => type.FullName == "System.Runtime.CompilerServices.IsExternalInit"))
            return "此属性仅能在初始化时设置。";
        return null;
    }

    private static bool IsScalar(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum || type == typeof(Guid)
            || Type.GetTypeCode(type) is not (TypeCode.Object or TypeCode.Empty or TypeCode.DBNull);
    }

    private static bool CanDescend(Type type) => type.IsClass
        && !(type.Namespace ?? string.Empty).StartsWith("System.", StringComparison.Ordinal);
}
