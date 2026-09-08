# VisiCore（视枢）桌面端 2.0.0

桌面端使用 .NET 10、WPF、CommunityToolkit.Mvvm 和依赖注入。窗口只处理窗口生命周期与显示事件，业务状态分布在 `ViewModels`，平台访问、认证、云台、事件和原生播放器分别由 `Services` 管理。`Models/Contracts.cs` 对应第二版具名 JSON 契约。

## 已实现的操作

- 1／4／9／16 分屏、拖放资源、单窗口放大、全屏、截图、声音和码流切换。H.264／H.265 交给 LibVLC 原生解码，默认优先使用 HTTPS MPEG-TS，旧会话未提供该地址时使用 HTTPS FLV；设置中可选择 RTSP 局域网兼容模式。
- 录像检索、真实区间时间轴、录像缺口、独立／统一暂停、继续、定位与倍速。定位后重建本地解码连接以清除旧缓冲。
- 权限范围内资源、完整分页、收藏同步、保存／修改／删除布局、共享轮巡。布局使用可空通道数组保存中间空窗口；轮巡方案只允许有效通道，离线或失权通道跳过且不启动播放。
- 报警筛选、图片、处理历史、认领、备注、关闭与重新打开。设备恢复状态独立显示。
- 异步导出任务、取消、重试、下载进度和下载取消。下载先写临时文件，完成后替换目标文件。
- 全部云台方向、自动扫描、变倍、聚焦、光圈和预置位。2 秒保活，松开、失焦、切换通道、退出及撤权停止；独占冲突不会停止其他值守员的控制。
- DPAPI 加密令牌、并发单飞续期、退出迟到保护、SignalR 事件刷新与定期数据同步。令牌稳定不变的续期同样受到并发版本保护。

## 构建与测试

```powershell
& "$env:LOCALAPPDATA/VideoPlatform/dotnet/dotnet.exe" build src/VideoPlatform.Desktop/VideoPlatform.Desktop.csproj -c Release
& "$env:LOCALAPPDATA/VideoPlatform/dotnet/dotnet.exe" test tools/VideoPlatform.Desktop.Tests/VideoPlatform.Desktop.Tests.csproj -c Release --filter 'Category!=Package'
& tools/v2-package.ps1 -VerifyInstallation
```

以上命令分别生成桌面端、执行客户端自动测试、生成 ZIP／MSI 并运行隔离安装升级验收。安装验收仅操作新建的独立测试产品，正式产品仍使用固定 UpgradeCode 和按计算机安装范围。

## 更新与存储

- 独立更新器必须与桌面端一起发布。安装包通过 HTTPS 下载，验证声明长度和 SHA-256，并在安装前再次检查缓存副本。
- 更新器从临时目录启动，等待桌面端正常退出，不强制结束其他程序。Windows Installer 禁用自动重启管理器关闭进程，安装失败由 MSI 回滚，普通更新失败后重新启动旧程序。
- `desktop-v2.json` 保存平台、传输偏好和最低版本；`session.bin` 仅保存当前 Windows 用户能够解密的会话。日志 `desktop.log` 采用 5 MB 轮换。
- 正常关闭窗口保留登录；主动退出撤销服务器会话并删除本地加密令牌。修改密码成功后重新登录。
- 当前交付无代码签名证书，不能标为已签名。客户端、播放器和更新器均不配置跳过 TLS 校验。

## 接口接入边界

平台根地址在线上使用 HTTPS；本机集成测试可以使用 HTTP。`PlatformApi` 将业务相对路径映射至生成客户端方法，所有媒体请求携带全局 `channelId`。`SessionService` 使用生成客户端认证，`ClientModelMapping` 映射界面模型，`GeneratedClientTransport` 统一处理中文错误；生成源位于 `src/VideoPlatform.Client`。

2026 年 9 月 7 日，生成客户端接入后的 71 项自动回归通过。候选实机验证通过正常证书校验登录，两路主码流均取得 2560×1440 原生截图，单窗口停止、其他窗口持续播放及退出释放通过。报告位于 `tools/VideoPlatform.Desktop.Tests/candidate-evidence`，该结果不代表全部设备型号或长时间稳定性验收。

自动渲染与安装验收的证据和实机检查项见 `tools/VideoPlatform.Desktop.Tests/验收说明.md`。渲染图只证明 WPF 布局和绑定，不代表真实录像机兼容性、原生视频窗口遮挡或 24 小时稳定性已通过。
