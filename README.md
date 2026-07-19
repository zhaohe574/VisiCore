# 视枢（VisiCore）

面向受控网络的视频资产、权限、ONVIF／RTSP 接入、实时会话、回放中继、PTZ、告警和审计平台。开源首版通过 Docker 运行，默认不包含任何厂商 SDK、设备凭据或预置设备。

## 能力边界

- 内置标准 ONVIF Profile S／G 与通用 RTSP 直连。
- 管理端提供资产、区域、账号、角色、设备插件、边缘节点、告警与审计管理。
- MediaMTX 和流网关在启用边缘 Profile 后提供受控流会话。
- `src/VisiCore.QtViewer` 是可选的 Windows 原生查看端源码，不包含在 Docker 安装中。
- 厂商专有能力通过独立、已签名的边缘插件扩展。核心 API 不下载、加载或执行上传的 DLL、脚本或容器镜像。

## 快速安装

前置条件：Docker Engine 与 Docker Compose v2。首次安装仅支持空数据库。

```powershell
Copy-Item .env.example .env
# 编辑 .env，至少替换 POSTGRES_PASSWORD
docker compose pull
docker compose up -d postgres
docker compose --profile setup run --rm setup
docker compose up -d
```

`setup` 会在交互式终端中创建首个系统管理员，密码不会写入 `.env`、镜像或日志。完成后访问 `http://127.0.0.1:8080` 登录管理端。

检查服务状态：

```powershell
docker compose ps
Invoke-WebRequest http://127.0.0.1:8080/healthz
```

停止并保留数据：`docker compose down`。彻底删除本地数据：`docker compose down -v`。

## 升级

`v0.2.0` 是视枢的新品牌基线，只支持全新安装，不提供 `v0.1.0` 的数据库、卷、浏览器会话或查看端本地设置升级路径。后续发行版升级前先备份 `postgres-data` 卷；完成备份后停止服务、拉取或构建新镜像并重新启动。已有数据库不得再次运行 `setup`，该容器会检测到已有账号后拒绝修改数据。

```powershell
docker compose down
docker compose pull
docker compose up -d
```

## 边缘与设备插件

启用 ONVIF／RTSP 边缘服务前，在 `.env` 中填写独立令牌和受控平台地址，再执行：

```powershell
docker compose --profile edge up -d --build
```

外部设备插件以独立仓库和 Compose 覆盖文件发布。管理员只能登记由部署侧信任公钥签名、镜像摘要和 SHA-256 固定的 manifest；插件容器不得挂载 Docker Socket，也不得获得 API 容器写权限。详细约定见 [设备插件说明](docs/设备插件.md)。

## 开发与质量

后端为 .NET 8，管理端为 React 与 TypeScript。常用检查：

```powershell
dotnet test .\tests\VisiCore.Api.Tests\VisiCore.Api.Tests.csproj --configuration Release
Push-Location .\src\VisiCore.Admin
npm ci
npm run typecheck
npm run build
Pop-Location
```

## 开源协作

- 许可证：[Apache-2.0](LICENSE)。
- 贡献要求：[CONTRIBUTING.md](CONTRIBUTING.md)，提交必须包含 DCO `Signed-off-by`。
- 漏洞请使用 GitHub Private Vulnerability Reporting，勿通过公开 Issue 披露。
- 第三方组件与许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
