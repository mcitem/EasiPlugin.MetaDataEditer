using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Cvte.Paint.Framework;

namespace Mcitem.EasiPlugin.MetaDataEditer;

/// <summary>通过元素实际实现的 ISavable&lt;T&gt; 读写，不扫描整个宿主程序集集合。</summary>
internal static class ElementSaveInfoAccessor
{
    public static ElementSaveInfo? TryGetSaveInfo(Element element)
    {
        foreach (var contract in GetSaveableContracts(element.GetType()))
        {
            try
            {
                if (contract.GetMethod("GetSaveInfo", Type.EmptyTypes)?.Invoke(element, null) is ElementSaveInfo result)
                    return result;
            }
            catch (Exception exception)
            {
                PluginLog.Exception($"读取 {element.GetType().Name} 元数据失败", exception);
            }
        }

        // 某些宿主元素由聚合 Provider 提供存储；从闭合后的返回类型获取方法。
        foreach (var method in typeof(ElementSaveInfoProviderExtensions).GetMethods(BindingFlags.Public | BindingFlags.Static)
                     .Where(method => method.Name == "GetElementSaveInfoProvider" && method.GetParameters().Length == 1))
        {
            foreach (var contract in GetSaveableContracts(element.GetType()))
            {
                try
                {
                    var closed = method.IsGenericMethodDefinition
                        ? method.MakeGenericMethod(contract.GetGenericArguments()[0]) : method;
                    if (!closed.GetParameters()[0].ParameterType.IsInstanceOfType(element)) continue;
                    var provider = closed.Invoke(null, new object[] { element });
                    if (provider is not null && closed.ReturnType.GetMethod("GetSaveInfo", Type.EmptyTypes)
                        ?.Invoke(provider, null) is ElementSaveInfo result) return result;
                }
                catch (Exception exception)
                {
                    PluginLog.Exception("读取聚合元数据失败", exception);
                }
            }
        }
        return null;
    }

    public static bool TrySetSaveInfo(Element element, ElementSaveInfo saveInfo)
    {
        foreach (var contract in GetSaveableContracts(element.GetType()))
        {
            var method = contract.GetMethod("SetSaveInfo");
            if (method is null || method.GetParameters().Length != 1
                || !method.GetParameters()[0].ParameterType.IsInstanceOfType(saveInfo)) continue;
            try
            {
                method.Invoke(element, new object[] { saveInfo });
                return true;
            }
            catch (Exception exception)
            {
                PluginLog.Exception($"应用 {element.GetType().Name} 元数据失败", exception);
                return false;
            }
        }
        PluginLog.Warn($"元素 {element.GetType().Name} 未提供兼容的元数据写入接口");
        return false;
    }

    private static IEnumerable<Type> GetSaveableContracts(Type elementType) =>
        elementType.GetInterfaces().Where(contract => contract.IsGenericType
            && contract.GetGenericTypeDefinition().FullName == "Cvte.Storage.ISavable`1"
            && typeof(ElementSaveInfo).IsAssignableFrom(contract.GetGenericArguments()[0]));
}
