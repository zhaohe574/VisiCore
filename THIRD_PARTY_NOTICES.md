# 第三方组件说明

运行时和构建产物依赖 .NET、PostgreSQL、Npgsql、Entity Framework Core、React、Vite、TypeScript、Lucide、MediaMTX、Nginx 与其各自依赖。具体版本以项目文件、锁文件和容器镜像摘要为准。

- .NET 与 ASP.NET Core：MIT。
- Entity Framework Core 与 Npgsql：MIT／PostgreSQL License，具体以各项目发布物为准。
- React、Vite、TypeScript、Lucide：MIT。
- PostgreSQL：PostgreSQL License。
- Nginx：BSD 2-Clause。
- MediaMTX：MIT。
- 可选 Qt 查看端及其运行时依赖不包含在 Docker 镜像中，使用者必须遵守其独立许可证。

发布流程应为镜像和发行附件生成 SBOM，并随 GitHub Release 一并提供。不得将厂商 SDK、许可证正文或私有二进制纳入本仓库、基础镜像或 SBOM 之外的发行物。
