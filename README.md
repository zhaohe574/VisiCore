# 京华安防平台

平台已完成设备接入、认证、媒体分发和管理后台基础闭环：

- `tools/hikvision_probe.py`：Python 版 Linux SDK 探针；
- `src/Hikvision.Adapter`：C# .NET 8 Linux x64 探针；
- `src/VideoPlatform.Api`：读取适配服务快照的最小 API；
- 已在 Ubuntu 26.04 上登录 DS-A72124R，读取通道并验证 1 路和 81 路预览码流回调。
- 服务器已安装 PostgreSQL 18，需按顺序执行 `database/001_initial.sql` 至最新迁移文件（当前为 `database/008_account_profile.sql`）。
- 平台 API 已支持本地账号登录、Bearer 会话、管理员 RBAC 和启动时设备／通道快照入库。
- 当前服务器数据库已有 1 台 DS-A72124R 设备和 81 路通道。
- 适配服务已启用报警布防和原始报文异步队列，平台提供报警查询／确认接口。
- 平台已提供 PTZ 权限代理，通道 8 已完成开始／停止实机短脉冲验证。
- 平台已提供车间、区域、机组读取和通道分配接口，当前 81 路通道均未分配。
- 平台已提供按用户绑定的回放会话，适配服务将 HCNetSDK 回放码流异步保存为临时 `.ps` 文件；实时预览已接入 ZLMediaKit 动态代理和短期令牌鉴权。
- Vue 管理后台已部署到服务器 Nginx，包含运行总览、设备与通道、实时预览、报警中心和业务结构页面；平台 API 已支持通道数据范围和桌面端版本发布链路。
- WPF 桌面端 Beta 已支持登录会话续期、API 地址持久化、车间／区域／机组／通道树、1／4／9／16 分屏、双击单窗口放大、全屏、截图、主／子码流、实时预览、录像检索、按录像真实时间回放、统一回放控制、PTZ 安全停止和 SignalR 报警提示。
- Web 管理端已支持报警服务端筛选、实时推送、媒体会话续期、播放器退避重连、全屏／截图、响应式预览和动态设备统计。
- 自动更新使用 MSI：独立更新器下载临时包、校验 SHA-256、等待桌面端退出后调用 `msiexec` 安装并重启；ZIP 和 EXE 仍用于手工下载。

## 本地网页预览

本轮修复版为 `1.0.1`。新增检查覆盖登录并发续期、媒体会话引用、WPF 分屏与退出清理、更新器哈希及测试 MSI 回滚。详细结果和实机边界见 `docs/开发状态.md`，回归命令见 `docs/桌面端构建与发布说明.md`。

打开 `http://localhost:4174/` 后，首页右上角锁图标、页脚“内部管理”或 `http://localhost:4174/#admin` 可进入管理端登录。用户名通常为 `admin`，密码使用部署服务器配置的管理员密码；登录页提示“账号或密码不正确”时才是凭据问题。

网页开发服务器默认代理到部署 API `https://10.37.200.74`。如果 API 在本机 5080 端口运行，可用 `VITE_API_PROXY_TARGET=http://127.0.0.1:5080` 覆盖代理目标。

## HTTPS 与桌面端连接

生产入口由 Nginx 监听 `80/443`，Nginx 使用 `deploy/nginx-video-platform.conf` 终止 TLS，再反向代理到仅监听本机的 API `127.0.0.1:5080`。不要把公网证书配置到 API 的 5080 端口，也不要让桌面端改用跳过证书校验的 HTTP 客户端。

当前测试服务器使用包含 `10.37.200.74` SAN 的内部自签名证书。Windows 客户端首次使用前，在项目根目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File tools/install-platform-certificate.ps1
```

导入后用 `https://10.37.200.74` 登录桌面端。新服务器绑定证书时，将证书公钥放到 `/etc/nginx/ssl/video-platform.crt`、私钥放到 `/etc/nginx/ssl/video-platform.key`，然后执行 `sudo nginx -t && sudo systemctl reload nginx`。正式公网环境应使用域名和受信任 CA 证书；内部自签名证书只适用于受控设备。

## C# 构建

```powershell
dotnet build src/Hikvision.Adapter/Hikvision.Adapter.csproj --configuration Release
dotnet publish src/Hikvision.Adapter/Hikvision.Adapter.csproj --configuration Release --runtime linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/hikvision-adapter-linux-x64
dotnet build VideoPlatform.sln --configuration Release
dotnet publish src/VideoPlatform.Api/VideoPlatform.Api.csproj --configuration Release --runtime linux-x64 --self-contained true -p:PublishSingleFile=true -o artifacts/api-linux-x64
dotnet publish src/VideoPlatform.Desktop/VideoPlatform.Desktop.csproj --configuration Release --runtime win-x64 --self-contained true -o artifacts/desktop-win-x64
dotnet publish src/VideoPlatform.Updater/VideoPlatform.Updater.csproj --configuration Release --runtime win-x64 --self-contained true -o artifacts/desktop-win-x64
wix build deploy/VideoPlatform.Desktop.wxs -arch x64 -o artifacts/VideoPlatform.Desktop-x64.msi
wix msi validate artifacts/VideoPlatform.Desktop-x64.msi
```

桌面端安装包发布细节见 `docs/桌面端构建与发布说明.md`。当前版本未加入 Windows 代码签名，正式分发前需要准备签名证书并接入发布流水线。

运行时使用环境变量提供设备凭据，不能把密码写入仓库。ZLMediaKit 已从 Gitee 源码构建并作为 systemd 服务运行；生产环境 HTTPS、正式安装包和代码签名仍按开发文档后续阶段实现。

服务器防火墙规则见 `deploy/ufw-video-platform.sh`，当前仅开放 SSH、HTTP 和客户端媒体端口。
