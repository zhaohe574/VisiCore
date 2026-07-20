# 第三方组件说明

运行时和构建产物依赖 .NET、PostgreSQL、Npgsql、Entity Framework Core、React、Vite、TypeScript、Lucide、MediaMTX、Nginx 与其各自依赖。具体版本以项目文件、锁文件和容器镜像摘要为准。

- .NET 与 ASP.NET Core：MIT。
- Entity Framework Core 与 Npgsql：MIT／PostgreSQL License，具体以各项目发布物为准。
- React、Vite、TypeScript、Lucide：MIT。
- PostgreSQL：PostgreSQL License。
- Nginx：BSD 2-Clause。
- MediaMTX：MIT。
- Windows 查看端 MSI 包含 Qt 运行时、Qt ADS、Qlementine、Lucide 资源与受控 libmpv 运行时。安装目录的 `licenses` 子目录保存 Qt、Qt ADS、Qlementine、Lucide 和受控 libmpv 的许可证文本；`visicore-viewer-runtime.json` 记录 libmpv 的版本、来源与 SHA-256。
- Qt：LGPL-3.0-only 或 Qt Commercial，取决于发行构建所使用的 Qt 许可证。Qt ADS、Qlementine、Lucide：MIT。libmpv：LGPL-2.1-or-later，具体以受控运行时随包许可证文本为准。

发布流程应为镜像和发行附件生成 SBOM，并随 GitHub Release 一并提供。不得将厂商 SDK、许可证正文或私有二进制纳入本仓库、基础镜像或 SBOM 之外的发行物。受控 libmpv 的许可证正文只在 MSI 阶段目录和发行安装包中交付，不写入仓库。
