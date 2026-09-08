# 桌面安装包发布

`v2-publish.py` 默认仅校验 `artifacts/v2-desktop/release-manifest.json` 及同目录 MSI／ZIP 的大小与 SHA256。需要 Python 3.10 以上；执行发布时另需本机 Paramiko、SSH 主机公钥信任记录，以及 `VIDEO_PLATFORM_SSH_PASSWORD` 环境变量。

```powershell
python tools/v2-publish.py --help
python tools/v2-publish.py --validate-only
python tools/v2-publish.py --execute --minimum-version 2.0.0 --notes 'VisiCore（视枢）桌面端 2.0.0'
```

前两条命令不会连接服务器；第三条会实际发布。执行结果以 `artifacts/v2-qa/publish` 内的报告为准；失败报告保留已完成的上传与发布记录，不能将部分发布视为整个流程成功。

默认使用 `liteware@10.37.200.74` 和 `https://10.37.200.74:8443`。管理员密码仅由远程工作进程读取 `/home/liteware/.config/video-platform/v2-production.json` 的 `adminPassword`，不会传回本机或写入报告。可用 `--host`、`--port`、`--user`、`--config`、`--api-base` 调整目标。SSH 使用本机 known_hosts，可用 `--known-hosts` 指定文件，或使用事先核对过的 `--host-key-sha256 SHA256:...`。HTTPS 始终校验证书和主机名；私有 CA 可用 `--ca-file /远程/受信任CA.pem`，或 `--local-ca-file artifacts/video-platform.crt` 指定已核实的公开证书。后者仅上传到本次临时目录，结束后清理，不提供跳过校验参数。

发布顺序为本地校验、SFTP 临时上传、远程再次校验、HTTPS 登录、检查已有版本、multipart API 上传并核对返回大小和 SHA256、发布 ZIP／MSI、下载验证、注销。最低版本默认 `2.0.0`，可用 `--minimum-version` 调整；`--force` 只对 MSI 开启强制更新，默认不开启。已有相同版本、包类型、SHA256、大小且发布策略一致的已发布记录直接跳过写入，并继续验证。已有匹配草稿可复用；同版本同类型存在不同内容或存在更高已发布版本时拒绝发布。

验证包含默认 latest 优先 MSI、packages 包含两个已发布包、按包类型选择、旧 latest 入口，以及新旧下载入口的 Range 206、完整文件大小与 SHA256。候选 API 返回同主机正式端口的 downloadUrl 时，验证保留路径并固定走所指定候选 HTTPS 端口。

报告默认写入 `artifacts/v2-qa/publish/唯一编号.json`，可用 `--report` 指定。报告保留失败阶段、已上传／发布的版本 ID 和清理／注销结果，不记录密码、令牌或原始 HTTP 正文。已发布记录不自动撤销，失败后可按相同参数重跑。远程进程处理超时及终止信号，自行注销和删除临时包文件，仅保留报告；本机取回报告后删除剩余临时目录。SSH 中断或远程工作仍在运行时，本机报告会明确标记清理未完成及临时目录，需恢复连接后核查，不能将此状态视为发布成功。`--timeout` 控制单次网络读写，`--remote-timeout` 默认 3600 秒，超时后另留最多 60 秒用于远程收尾。

## 桌面 2.0.1 正式产物

最终清单为 `artifacts/v2-desktop-2.0.1/release-manifest.json`，不能使用 `artifacts/v2-desktop` 中的历史开发包。打包脚本从桌面项目 XML 读取版本号，支持 `-OutputDirectory` 指定独立产物目录。

```powershell
& tools/v2-package.ps1 -SkipPublish -VerifyInstallation -OutputDirectory artifacts/v2-desktop-2.0.1
python tools/v2-publish.py --execute --manifest artifacts/v2-desktop-2.0.1/release-manifest.json --api-base https://10.37.200.74 --local-ca-file artifacts/video-platform.crt --minimum-version 2.0.0
```

第一条要求指定目录内的 `win-x64` 已完成桌面发布并包含更新器。第二条按照最终清单发布，保持 TLS 校验；同内容、同策略重跑可幂等复核。2026-09-07 已发布 MSI（ID 5）和 ZIP（ID 4），报告 `artifacts/v2-qa/publish/release-2.0.1-production.json` 完整通过，两个包均未签名。
