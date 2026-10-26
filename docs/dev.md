## 开发与构建

需要 Windows 10 或更高版本、支持 .NET 6/WPF 的 Visual Studio 或 .NET SDK，以及已安装并至少启动过一次的希沃白板 EasiNote 5。构建期间 SDK 会解析本机宿主程序集；在没有安装宿主的通用 GitHub runner 上不能直接完成构建。

打开 `EasiPlugin.MetaDataEditer.sln`，或运行：

```powershell
dotnet restore EasiPlugin.MetaDataEditer.sln
dotnet build EasiPlugin.MetaDataEditer.sln -c Release
```

项目文件位于 `src/EasiPlugin.MetaDataEditer/EasiPlugin.MetaDataEditer.csproj`。目标框架保留 `net6.0-windows7.0`，SDK 为 `dotnetCampus.EasiPlugin.Sdk 2.1.1-alpha.4`，宿主引用级别为 `core`。TFM 名称不等于对所有 Windows 7 环境的支持保证。

NuGet 配置仅使用官方公共源，不要求仓库中存在本机 `packages` 文件夹。SDK 包及宿主 DLL 不提交到源码仓库。

Release 产物位于 `bin/Release`：

- `元素元数据编辑器.1.2.0.exe`
- `元素元数据编辑器.1.2.0.zip`
- `EasiPlugin.MetaDataEditer.1.2.0.enp`

文件夹发布配置使用 `bin/$(Configuration)/$(TargetFramework)/publish/`。调试启动配置由 SDK 根据本机安装生成，不提交到 Git。安装包通过 GitHub Releases 单独分发，不能把整个 `bin` 目录加入源码提交。

## 异步、取消与宿主边界

- `Program.OnRunningAsync` 等待实际的 UI 服务，观察整个初始化任务的异常；宿主 Dispatcher 关闭时取消等待。
- `PluginCommand` 为同步 ICommand 提供异步执行边界，阻止同一个命令重复执行，捕获取消和异常。WPF 的 Loaded、字段变化等 void 事件处理器也完整观察其异步任务。
- 属性 getter 可能依赖 WPF，因此读取和反射遍历始终在 UI 线程执行。遍历按约 8 ms 的时间片让出 Dispatcher，并限制递归深度、每个集合数量及总字段数。
- 只涉及托管字段描述的排序和索引、独立安装器的进程/目录检测在后台执行，并传递 CancellationToken。
- 关闭窗口取消未完成的准备工作。保存时先构建工作副本，最后在 UI 线程通过宿主 SetSaveInfo 提交。提交前检查取消；宿主同步提交没有取消契约，不能在中途强制中断。
- 日志通过有界队列后台异步写入。日志失败不向宿主传播异常，不记录字段值、进程命令行或异常消息。退出时日志为尽力写入，进程突然结束可能丢失末尾少量记录。
- 不接管宿主全局未处理异常，不使用强杀进程或跨线程访问宿主对象的方式“恢复”错误。

插件异常边界可降低未处理异常进入宿主的风险，但不能替代宿主内部 API 的兼容性验证。GetSaveInfo、Clone、SetSaveInfo 是宿主提供的同步调用，无法保证单次调用的耗时或中途取消能力。

## 日志与故障排查

插件日志位于：

```text
%AppData%\Seewo\EasiNote5\Plugins\EasiPlugin.MetaDataEditer\Logs\EasiPlugin.MetaDataEditer.<pid>.log
```

宿主加载记录位于 `%AppData%\Seewo\EasiNote5\Log\all_info.txt` 中的 `[LoadExtension]`。菜单未出现时，检查插件是否加载、运行模式、选中元素和注册日志。构建成功不代表已经运行的宿主加载了新版；请保存课件后安全退出并重新启动。

## 源码与发布检查

构建所需源码、XAML、解决方案、`Directory.Build.props`、`build/Version.props`、`NuGet.config` 和不含本机信息的发布配置应纳入 Git。

`.vs`、`bin`、`obj`、本机 agent 记忆、调试配置、日志、密钥和安装包应保持忽略。发布分支为 `codex/github-release`，以当前源码为准，不包含旧项目历史或 CHANGELOG。安装包同时包含 LICENSE 和本 README，保留下方的许可证及上游来源说明。

按维护者要求，本次不添加或执行测试。构建通过只能确认编译与打包，不代表已经完成实际宿主的安装、取消、保存和撤销验证。
