# VisiCore（视枢）桌面端 2.1.0

桌面端使用 .NET 10、WPF、CommunityToolkit.Mvvm 和依赖注入。窗口只处理窗口生命周期与显示事件，业务状态分布在 `ViewModels`，平台访问、认证、云台、事件和原生播放器分别由 `Services` 管理。`Models/Contracts.cs` 对应第二版具名 JSON 契约。

## 主题与界面结构（2.1.0）

- `Themes/Light.xaml`、`Themes/Dark.xaml` 提供同一组颜色令牌，`Themes/Controls.xaml` 的控件模板全部通过 `DynamicResource` 引用令牌，`Services/ThemeService.cs` 在运行时替换 App 资源中位置 0 的主题字典即可整体换肤。默认浅色，设置页可切换深色；视频画面区在两套主题下都保持深色画布。
- 外壳：顶部工具条（品牌 / 一级模块选项卡 / 连接状态 / 刷新 / 用户菜单 / 窗口控制）+ 左侧模块功能竖栏 + 底部状态栏（模块、连接、诊断、版本、时钟）。
- `Views/WorkspaceView.xaml` 为媒体工作台：左侧资源面板（设备分组 / 组织分组切换、树右键菜单、方案设置抽屉）、中央视频墙、右侧云台抽屉、底部标准控制栏、回放时间轴。`Views/VideoWallPanel.cs` 按格位跨度排布，支持 1 / 4 / 9 / 16 与 1+5、1+7 聚焦档位。

## 已实现的操作

- 界面布局与使用逻辑仿照海康威视 iVMS-4200：顶部模块导航与功能竖栏、左侧设备资源树（录像机 → 通道，带通道号与在线统计）、主预览视图标签栏（点击切换已保存视图、新建／保存视图、一键轮巡开关）、1／4／9／16／1+5／1+7 分屏、单格悬浮跟踪条、右侧八向云台罗盘与镜头控制、底部标准控制栏、远程回放左侧日历检索面板与居中回放控制条、报警中心实况／录像联动、顶部用户下拉菜单与状态栏时钟。
- 拖放资源、单窗口放大、全屏、截图、声音和码流切换（右键菜单与单格跟踪条均可切换，人工档位优先于自动档位）。H.264／H.265 交给 LibVLC 原生解码，默认优先使用 HTTPS MPEG-TS，旧会话未提供该地址时使用 HTTPS FLV；设置中可选择 RTSP 局域网兼容模式。
- 录像检索、真实区间时间轴、录像缺口、独立／统一暂停、继续、定位与倍速。定位后重建本地解码连接以清除旧缓冲。
- 权限范围内资源、完整分页、收藏同步、保存／修改／删除布局、共享轮巡。布局使用可空通道数组保存中间空窗口；轮巡方案只允许有效通道，离线或失权通道跳过且不启动播放。
- 报警筛选、图片、处理历史、认领、备注、关闭与重新打开。设备恢复状态独立显示。
- 异步导出任务、取消、重试、下载进度和下载取消。下载先写临时文件，完成后替换目标文件。
- 全部云台方向、自动扫描、变倍、聚焦、光圈和预置位。2 秒保活，松开、失焦、切换通道、退出及撤权停止；独占冲突不会停止其他值守员的控制。
- DPAPI 加密令牌、并发单飞续期、退出迟到保护、SignalR 事件刷新与定期数据同步。

## 性能设计（2.1.0）

| 措施 | 说明 |
| --- | --- |
| 显示档位驱动码流 | 格子首开取子码流；单窗放大与全屏升主码流，退出聚焦降回子码流；设备无子码流时标记后不再重试。 |
| 非可见格位不占解码器 | 放大器/全屏下的其它窗口进入 `Hidden` 档位并停止会话；离屏 `VideoTileView` 释放原生视频窗口。 |
| 渲染降层 | 全屏控制条为视觉树内覆盖层（非透明顶层 Popup），无软阴影；每格覆盖层合并为一层。 |
| 热路径收敛 | `ForegroundWindow` 反射结果按类型缓存；可见集合未变化时不重置集合；鼠标移动不再逐帧做屏幕坐标换算。 |
| 刷新节流 | 时钟独立 1 秒计时器；业务拍子 5 秒；数据校对 60 秒且仅在数据签名变化时重建资源树；回放播放头 1 秒节流。 |
| 播放参数可调 | 网络缓存（默认 800ms）与 D3D11 硬件解码在设置页可调，硬解不可用时由 libVLC 自动回退。 |

## 测量与验收工具

```powershell
# 采样桌面端 CPU / GPU / 内存 / 线程 / 句柄 / 媒体会话数，产出可对比 JSON 报告
pwsh -File tools/v2-desktop-profile.ps1 -Label "16路-子码流" -DurationSeconds 120 -Notes "子码流"
```

`tools/v2-desktop-profile.ps1` 只做只读采样：CPU 取相邻 `TotalProcessorTime` 差值除以墙钟与逻辑核数，GPU 取 `\GPU Engine(*)\Utilization Percentage` 中属于目标进程 PID 的引擎实例求和。无权限读取 GPU 计数器时记录 `gpuAvailable=false`，UI 延迟未接入探针时记为 `null`，都不以零或估计值顶替。

**注意**：`Process.TotalProcessorTime` 会累计子进程时间，跨进程对比会得到远大于真实值的读数；做前后对比时应以 `\Process(<名称>)\% Processor Time` 除以逻辑核数为主口径。

### 压力测试模式

用于可复现地采集真实负载。仅当设置 `VISICORE_STRESS=1` 时生效，未设置时本机制不产生任何行为、也不读取凭据：

| 变量 | 含义 |
| --- | --- |
| `VISICORE_STRESS` | 必须为 `1` 才启用 |
| `VISICORE_STRESS_USER` / `VISICORE_STRESS_PASSWORD` | 登录账号与口令（只进入子进程环境，不落盘、不写日志） |
| `VISICORE_STRESS_LAYOUT` | 分屏档位：`1`／`4`／`9`／`16`／`1+5`／`1+7` |
| `VISICORE_STRESS_SECONDS` | 加载完成后的运行秒数，到时自动退出；`0` 表示不自动退出 |

该模式跳过“必须更新后才能登录”的限制（目标平台可能仍在运行被撤回的旧版本），界面登录仍保持强制校验。完整复现步骤与实测数据见 `docs/v2-桌面2.1.0界面复核.md`。

## 构建与测试

```powershell
& "$env:LOCALAPPDATA/VideoPlatform/dotnet/dotnet.exe" build src/VideoPlatform.Desktop/VideoPlatform.Desktop.csproj -c Release
& "$env:LOCALAPPDATA/VideoPlatform/dotnet/dotnet.exe" test tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj -c Release --filter 'Category!=Package'
& tools/v2-package.ps1 -VerifyInstallation
```

以上命令分别生成桌面端、执行客户端自动测试、生成 ZIP／MSI 并运行隔离安装升级验收。安装验收仅操作新建的独立测试产品，正式产品仍使用固定 UpgradeCode 和按计算机安装范围。`VisualSmokeTests` 会在 `tools/VideoPlatform.Desktop.Tests/screenshots` 输出 30 张渲染图（主预览 / 1+7 聚焦 / 回放 / 报警 / 导出 × 两档分辨率 × 三档 DPI），用于人工核对布局。

## 更新与存储

- 独立更新器必须与桌面端一起发布。安装包通过 HTTPS 下载，验证声明长度和 SHA-256，并在安装前再次检查缓存副本。
- 更新器从临时目录启动，等待桌面端正常退出，不强制结束其他程序。Windows Installer 禁用自动重启管理器关闭进程，安装失败由 MSI 回滚，普通更新失败后重新启动旧程序。
- `desktop-v2.json` 保存平台、传输偏好、主题、显示与性能参数和最低版本；`session.bin` 仅保存当前 Windows 用户能够解密的会话。日志 `desktop.log` 采用 5 MB 轮换。
- 正常关闭窗口保留登录；主动退出撤销服务器会话并删除本地加密令牌。修改密码成功后重新登录。
- 当前交付无代码签名证书，不能标为已签名。客户端、播放器和更新器均不配置跳过 TLS 校验。

## 接口接入边界

平台根地址在线上使用 HTTPS；本机集成测试可以使用 HTTP。`PlatformApi` 将业务相对路径映射至生成客户端方法，所有媒体请求携带全局 `channelId`。`SessionService` 使用生成客户端认证，`ClientModelMapping` 映射界面模型，`GeneratedClientTransport` 统一处理中文错误；生成源位于 `src/VideoPlatform.Client`。

2026 年 9 月 7 日，生成客户端接入后的 71 项自动回归通过。候选实机验证通过正常证书校验登录，两路主码流均取得 2560×1440 原生截图，单窗口停止、其他窗口持续播放及退出释放通过。报告位于 `tools/VideoPlatform.Desktop.Tests/candidate-evidence`，该结果不代表全部设备型号或长时间稳定性验收。

自动渲染与安装验收的证据和实机检查项见 `tools/VideoPlatform.Desktop.Tests/验收说明.md`，界面逐项复核见 `docs/v2-桌面2.1.0界面复核.md`。渲染图只证明 WPF 布局和绑定，不代表真实录像机兼容性、原生视频窗口遮挡或 24 小时稳定性已通过。**2.1.0 的 CPU／显卡占用改善需要按复核文档用 `tools/v2-desktop-profile.ps1` 在真实设备上采样记录，当前尚未完成。**

