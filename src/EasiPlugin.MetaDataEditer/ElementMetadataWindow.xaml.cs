using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Cvte.Paint.Framework;
using Cvte.Windows.Controls;

namespace Mcitem.EasiPlugin.MetaDataEditer;

public partial class ElementMetadataWindow : SafeWindow
{
    private readonly ViewModel _viewModel;

    public ElementMetadataWindow(Element element, int selectionCount = 1)
    {
        InitializeComponent();
        _viewModel = new ViewModel(this, element, selectionCount);
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    // WPF 事件签名要求 void，完整观察初始化任务及其异常。
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        try
        {
            Loaded -= OnLoaded;
            await _viewModel.InitializeAsync();
        }
        catch (OperationCanceledException) when (_viewModel.IsClosed) { }
        catch (Exception exception)
        {
            PluginError.Report("读取元素元数据失败", exception, this);
            try { if (IsVisible) Close(); }
            catch (Exception closeException) { PluginLog.Exception("关闭元数据窗口失败", closeException); }
        }
    }

    private sealed class ViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly ElementMetadataWindow _window;
        private readonly Element _element;
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _cancellationToken;
        private ElementSaveInfo? _original;
        private IReadOnlyList<SaveInfoField> _fields = Array.Empty<SaveInfoField>();
        private string _filterText = string.Empty;
        private string _category = "全部分类";
        private bool _onlyEditable;
        private bool _onlyModified;
        private SaveInfoField? _selectedField;
        private bool _refreshPending;
        private bool _disposed;
        private bool _busy = true;
        private string _activity = "正在读取元数据，可随时取消…";

        public ViewModel(ElementMetadataWindow window, Element element, int selectionCount)
        {
            _window = window;
            _element = element;
            _cancellationToken = _lifetime.Token;
            ElementTitle = element.GetType().Name switch
            {
                "TextElement" => "文本元素", "ImageElement" => "图片元素", "GroupElement" => "组合元素",
                _ => element.GetType().Name,
            };
            ElementIdentity = $"{element.GetType().Name} · ID {element.Id}"
                + (selectionCount > 1 ? $" · 已选 {selectionCount} 个，本次编辑第一个" : string.Empty);
            Fields = CollectionViewSource.GetDefaultView(_fields);
            SaveCommand = new PluginCommand(SaveAsync,
                () => CanEdit && _fields.Any(item => item.IsModified) && !_fields.Any(item => item.HasError), _cancellationToken);
            CancelCommand = new PluginCommand(_ => { _window.Close(); return Task.CompletedTask; });
            RevertCommand = new PluginCommand(_ => { Revert(); return Task.CompletedTask; },
                () => CanEdit && _fields.Any(item => item.IsModified), _cancellationToken);
        }

        public async Task InitializeAsync()
        {
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();
                // 宿主 API 没有异步/取消契约，不能在后台线程访问宿主元素。
                _original = ElementSaveInfoAccessor.TryGetSaveInfo(_element)
                    ?? throw new InvalidOperationException("此元素未提供可读取的元数据。");
                _fields = await SaveInfoFieldExplorer.ExploreAsync(_original, _window.Dispatcher, _cancellationToken);
                _cancellationToken.ThrowIfCancellationRequested();
                Categories = new[] { "全部分类" }.Concat(_fields.Select(item => item.Category).Distinct()).ToArray();
                Fields = CollectionViewSource.GetDefaultView(_fields);
                Fields.Filter = item => Matches((SaveInfoField)item);
                foreach (var item in _fields) item.PropertyChanged += FieldChanged;
                SelectedField = _fields.FirstOrDefault();
                Changed(nameof(Categories));
                Changed(nameof(Fields));
                Changed(nameof(Summary));
            }
            finally { SetBusy(false); }
        }

        public bool IsClosed => _disposed;
        public bool CanEdit => !_busy && !_disposed;
        public string ElementTitle { get; }
        public string ElementIdentity { get; }
        public string Summary => $"共 {_fields.Count} 项属性 · {_fields.Count(item => item.IsEditable)} 项可编辑 · {_fields.Count(item => item.IsReadOnly)} 项只读";
        public string[] Categories { get; private set; } = new[] { "全部分类" };
        public ICollectionView Fields { get; private set; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RevertCommand { get; }
        public int VisibleCount => Fields.Cast<object>().Count();
        public string Status => _busy ? _activity : $"显示 {VisibleCount} / {_fields.Count} 项 · 已修改 {_fields.Count(item => item.IsModified)} 项"
            + (_fields.Any(item => item.HasError) ? " · 请修正格式错误" : string.Empty);

        public string FilterText { get => _filterText; set { _filterText = value; Changed(); Refresh(); } }
        public string Category { get => _category; set { _category = value; Changed(); Refresh(); } }
        public bool OnlyEditable { get => _onlyEditable; set { _onlyEditable = value; Changed(); Refresh(); } }
        public bool OnlyModified { get => _onlyModified; set { _onlyModified = value; Changed(); Refresh(); } }
        public SaveInfoField? SelectedField { get => _selectedField; set { _selectedField = value; Changed(); } }
        public event PropertyChangedEventHandler? PropertyChanged;

        private bool Matches(SaveInfoField item) => (!OnlyEditable || item.IsEditable)
            && (!OnlyModified || item.IsModified) && (Category == "全部分类" || item.Category == Category)
            && (string.IsNullOrWhiteSpace(FilterText) || new[] { item.Path, item.DisplayName, item.TypeDisplayName }
                .Any(text => text.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase)));

        private void Refresh()
        {
            if (_disposed) return;
            try
            {
                Fields.Refresh();
                if (SelectedField is null || !Fields.Contains(SelectedField)) SelectedField = Fields.Cast<SaveInfoField>().FirstOrDefault();
                Changed(nameof(VisibleCount));
                Changed(nameof(Status));
            }
            catch (Exception exception) { PluginError.Report("刷新属性列表失败", exception, _window); }
        }

        private async void FieldChanged(object? sender, PropertyChangedEventArgs args)
        {
            try
            {
                if (_disposed || args.PropertyName != nameof(SaveInfoField.IsModified)) return;
                Changed(nameof(Status));
                CommandManager.InvalidateRequerySuggested();
                if (!OnlyModified || _refreshPending) return;
                _refreshPending = true;
                try
                {
                    await _window.Dispatcher.InvokeAsync(Refresh, DispatcherPriority.Background, _cancellationToken);
                }
                finally { _refreshPending = false; }
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) { PluginError.Report("更新字段状态失败", exception, _window); }
        }

        private void Revert()
        {
            foreach (var item in _fields) item.Revert();
            Refresh();
        }

        private async Task SaveAsync(CancellationToken cancellationToken)
        {
            if (_original is null || _fields.Any(item => item.HasError)) return;
            SetBusy(true, "正在准备保存，可随时取消…");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Clone 及 SetSaveInfo 是宿主同步 API，只能在 UI 线程调用。
                var workingCopy = _original.Clone();
                if (ReferenceEquals(workingCopy, _original))
                    throw new InvalidOperationException("宿主未能创建独立的元数据副本。");
                workingCopy.Id = _original.Id;
                workingCopy.IsLocked = _original.IsLocked;
                workingCopy.CanClone = _original.CanClone;
                var targetFields = await SaveInfoFieldExplorer.ExploreAsync(workingCopy, _window.Dispatcher, cancellationToken);
                var targets = await Task.Run(() => targetFields.ToDictionary(item => item.Path), cancellationToken);
                // 先恢复 Clone 重置的可编辑属性，再应用用户输入，避免共享引用路径覆盖已修改值。
                var edits = _fields.Where(item => item.IsEditable).OrderBy(item => item.IsModified).ToArray();
                for (var index = 0; index < edits.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var item = edits[index];
                    if (!targets.TryGetValue(item.Path, out var target) || target.IsReadOnly)
                        throw new InvalidOperationException("字段无法写入：" + item.Path);
                    target.Value = item.Value;
                    target.Apply();
                    if (index % 32 == 31)
                        await _window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background, cancellationToken);
                }
                // 提交前最后一次检查取消；宿主提交视为不可拆分操作，不跨线程或中途取消。
                cancellationToken.ThrowIfCancellationRequested();
                if (!ElementSaveInfoAccessor.TrySetSaveInfo(_element, workingCopy))
                    throw new InvalidOperationException("宿主未能应用更改。");
                PluginLog.Info($"元素元数据已更新，修改 {_fields.Count(item => item.IsModified)} 项");
                _window.DialogResult = true;
            }
            finally { SetBusy(false); }
        }

        private void SetBusy(bool busy, string? activity = null)
        {
            _busy = busy;
            if (activity is not null) _activity = activity;
            if (_disposed) return;
            Changed(nameof(CanEdit));
            Changed(nameof(Status));
            Changed(nameof(VisibleCount));
            CommandManager.InvalidateRequerySuggested();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _lifetime.Cancel(); }
            catch (Exception exception) { PluginLog.Exception("取消窗口后台工作失败", exception); }
            finally
            {
                foreach (var item in _fields) item.PropertyChanged -= FieldChanged;
                _lifetime.Dispose();
            }
        }

        private void Changed([CallerMemberName] string? property = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
