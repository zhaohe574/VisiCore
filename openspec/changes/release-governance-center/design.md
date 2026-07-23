# 设计

平台在已验签的统一发行描述之外保存一条不可修改的治理记录。该记录只保存关联 change ID、来源提交文档和 GitHub Release／Actions／证据外链；所有链接必须属于配置的 VisiCore GitHub 仓库。管理端仅展示这些记录和既有升级计划。

OpenSpec 门禁在 Pull Request 中识别高影响路径，并要求 PR 正文声明 change ID。RC 与 stable 校验发行清单所引用 change 均已验证完成。
