# 制品版本

根目录 `VERSION` 是整套 GitHub Release 的发行批次，标签必须与它一致。各端制品使用独立版本文件，可以在同一发行批次中保持不同版本：

- `core.txt`：核心容器、核心 API、Core Host Agent 及核心 .NET 组件。
- `edge.txt`：Linux Edge 容器、Windows Edge Node、Edge Agent、Edge Host Agent 与配套工具。
- `viewer.txt`：Windows Viewer MSI 与 Qt 查看端。
- `src/VisiCore.Admin/package.json`：管理端 Web 静态资源。

发布前递增实际变更端的版本。根 `VERSION` 仅在需要创建整套签名发行描述和 GitHub Release 时递增；它不再强制等于任何单端版本。
