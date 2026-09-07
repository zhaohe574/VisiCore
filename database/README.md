# 数据库迁移

`001_initial.sql` 是 PostgreSQL 首版核心表，`002_auth.sql` 增加会话和首版权限，`003_alarm_dedup.sql` 增加报警事件去重哈希，`004_channel_scopes.sql` 增加角色和用户通道数据范围，`005_desktop_releases.sql` 增加桌面端版本发布，`006_alarm_details.sql` 增加报警图片数据列，`007_channel_ptz.sql` 为已有数据库补充通道云台能力字段，`008_account_profile.sql` 增加账号姓名、手机号及索引。当前 Ubuntu 26.04 使用 PostgreSQL 18，SQL 保持 PostgreSQL 16 兼容；生产环境按文件顺序执行迁移。

报警适配服务写入 `alarm-events.ndjson`，平台服务后台异步导入 `alarm_events`，避免在 HCNetSDK 回调线程中访问数据库。

设备账号、密码和 SDK 凭据不进入数据库。

数据范围按角色或用户保存；范围类型可直接绑定车间、区域、机组或通道。服务端在通道列表、实时预览、录像、回放、PTZ 和报警查询入口统一校验，未配置范围的管理员保持全量访问。
