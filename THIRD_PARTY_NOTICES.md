# 第三方组件说明

运行时和构建产物依赖 .NET、PostgreSQL、Npgsql、Entity Framework Core、React、Vite、TypeScript、Lucide、MediaMTX、Nginx 与其各自依赖。核心单容器固定使用 PostgreSQL `18.4-bookworm` 与 MediaMTX `1.19.2`；其他具体版本以项目文件、锁文件和镜像摘要为准。

- .NET 与 ASP.NET Core：MIT。
- Entity Framework Core 与 Npgsql：MIT／PostgreSQL License，具体以各项目发布物为准。
- React、Vite、TypeScript、Lucide：MIT。
- PostgreSQL：PostgreSQL License。
- Nginx：BSD 2-Clause。
- MediaMTX：MIT。
- Windows 查看端 MSI 包含 Qt 运行时、MinGW 运行库、Qt ADS、Qlementine、Lucide 资源与受控 libmpv 运行时。安装目录的 `licenses` 子目录保存 Qt LGPL-3.0-only、MinGW、Qt ADS、Qlementine、Lucide 和受控 libmpv 的许可证文本；`visicore-viewer-runtime.json` 记录 libmpv 的版本、来源与 SHA-256。
- 正式 Release 使用开源 Qt 6.10.3，Qt：LGPL-3.0-only。Qt ADS、Qlementine、Lucide：MIT。MinGW 运行库与 libmpv 的具体许可分别以安装包随附许可证文本为准。

发布流程应为镜像和发行附件生成 SBOM，并随 GitHub Release 一并提供。不得将厂商 SDK、许可证正文或私有二进制纳入本仓库、基础镜像或 SBOM 之外的发行物。受控 libmpv 的许可证正文只在 MSI 阶段目录和发行安装包中交付，不写入仓库。
