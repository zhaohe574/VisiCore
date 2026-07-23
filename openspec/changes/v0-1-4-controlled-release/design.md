# 设计

RC 发布调用完整质量门禁与三平台 staging 演练。staging 汇总证据经 RSA-PSS 签名并附加到候选 Release；stable 工作流在 production 审批后复验候选摘要、签名与 staging 证据，再为同一 OCI digest 创建正式标签与 `latest`。
