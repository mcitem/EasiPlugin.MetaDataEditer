using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace Mcitem.EasiPlugin.MetaDataEditer;

/// <summary>输入仅保存在编辑状态中，确认保存时才应用到工作副本。</summary>
internal sealed class SaveInfoField : INotifyPropertyChanged
{
    private readonly PropertyInfo? _property;
    private readonly object _owner;
    private readonly string _originalText;
    private string _value;

    public SaveInfoField(string path, Type valueType, object? value, object owner,
        PropertyInfo? property = null, string? readOnlyReason = null)
    {
        Path = path;
        ValueType = valueType;
        OriginalValue = value;
        _owner = owner;
        _property = property;
        ReadOnlyReason = readOnlyReason ?? string.Empty;
        try { _originalText = FormatValue(value); }
        catch (Exception exception)
        {
            _originalText = "（无法显示）";
            ReadOnlyReason = "此属性无法安全转换为文本。";
            PluginLog.Exception("格式化元数据属性失败", exception);
        }
        _value = _originalText;
    }

    public string Path { get; }
    public string DisplayName => _property?.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName
        ?? FieldName(Path[(Path.LastIndexOf('.') + 1)..]);
    public Type ValueType { get; }
    public object? OriginalValue { get; }
    public string OriginalText => _originalText;
    public string ReadOnlyReason { get; }
    public bool IsReadOnly => ReadOnlyReason.Length > 0;
    public bool IsEditable => !IsReadOnly;
    public string TypeDisplayName => FriendlyTypeName(ValueType);
    public string Category => Path.Split('.')[0] switch
    {
        "Id" or "Level" => "标识",
        "X" or "Y" or "Width" or "Height" or "Rotation" or "RotateOrigin" or "Transform" => "位置与尺寸",
        "RichText" or "Text" => "文字与排版",
        "ElementHyperlink" or "Animations" or "ElementBehavior" => "交互与动画",
        _ => "其他属性",
    };

    public string Value
    {
        get => _value;
        set
        {
            if (IsReadOnly || value == _value) return;
            _value = value;
            Notify(nameof(Value));
            Notify(nameof(IsModified));
            Notify(nameof(HasError));
            Notify(nameof(ErrorText));
            Notify(nameof(StatusText));
        }
    }

    public bool IsModified => !IsReadOnly && Value != OriginalText
        && (!TryParse(out var parsed) || !Equals(parsed, OriginalValue));
    public bool HasError => !IsReadOnly && Value != OriginalText && !TryParse(out _);
    public string ErrorText => HasError ? $"请输入有效的 {TypeDisplayName} 值。可空类型可使用 <null>。" : string.Empty;
    public string StatusText => IsReadOnly ? "只读" : HasError ? "格式错误" : IsModified ? "已修改" : "可编辑";
    public string Description => IsReadOnly ? ReadOnlyReason :
        (Nullable.GetUnderlyingType(ValueType) ?? ValueType).IsEnum
            ? "可选值：" + string.Join("、", Enum.GetNames(Nullable.GetUnderlyingType(ValueType) ?? ValueType))
            : ValueType == typeof(bool) ? "可输入 true、false、1 或 0。"
            : "修改在点击“保存更改”后应用；保存课件后会随课件保留。";

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool TryParse(out object? result)
    {
        var nullable = Nullable.GetUnderlyingType(ValueType);
        var type = nullable ?? ValueType;
        result = null;
        if ((nullable is not null || ValueType == typeof(string)) && Value == "<null>") return true;
        if (nullable is not null && string.IsNullOrWhiteSpace(Value)) return true;
        try
        {
            if (type == typeof(string)) result = Value;
            else if (type == typeof(Guid)) result = Guid.Parse(Value);
            else if (type == typeof(DateTime)) result = DateTime.Parse(Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            else if (type.IsEnum) result = Enum.Parse(type, Value, ignoreCase: true);
            else if (type == typeof(bool)) result = Value.Trim() switch
            {
                "1" => true,
                "0" => false,
                _ => bool.Parse(Value),
            };
            else result = Convert.ChangeType(Value, type, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    public void Apply()
    {
        if (!IsModified) return;
        if (IsReadOnly || _property is null || !TryParse(out var value))
            throw new InvalidOperationException($"字段 {Path} 的值无法应用。");
        _property.SetValue(_owner, value);
    }

    public void Revert() => Value = OriginalText;
    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    private static string FormatValue(object? value) => value switch
    {
        null => "<null>",
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty,
    };

    public static string FriendlyTypeName(Type type) => Nullable.GetUnderlyingType(type) is { } underlying
        ? FriendlyTypeName(underlying) + "?"
        : type.Name switch
        {
            "String" => "文本", "Boolean" => "布尔值", "Double" => "小数 (double)",
            "Single" => "小数 (float)", "Int32" => "整数", "Int64" => "长整数",
            _ => type.Name,
        };

    private static string FieldName(string name) => name switch
    {
        "Id" => "元素标识", "X" => "水平位置", "Y" => "垂直位置", "Width" => "宽度", "Height" => "高度",
        "Rotation" => "旋转角度", "Level" => "层级", "IsLocked" => "锁定", "ElementHyperlink" => "超链接地址",
        "Text" => "文本内容", "FontSize" => "字号", "FontFamily" => "字体", "IsErasable" => "允许擦除",
        "CanClone" => "允许复制", "HasMask" => "遮罩", "LineSpacing" => "行距", "FixedLineSpacing" => "固定行距",
        "TextAlignment" => "段落对齐", "VerticalTextAlignment" => "垂直对齐", _ => name,
    };
}
