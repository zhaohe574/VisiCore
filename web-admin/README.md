# VisiCore（视枢） Web 端

Vue 3、Vue Router、Pinia、Element Plus，版本为 2.0.0。管理端采用浅色工作区，实时视频与录像回放使用独立深灰视频墙。

## 开发与验证

```powershell
npm install
npm run dev
npm test
npm run build
npm run generate:api
```

以上命令分别安装依赖、启动开发服务、运行核心逻辑测试、生成生产构建和从真实 OpenAPI 生成接口类型。生产文件输出到 `dist`。`generate:api` 读取主实施者生成的 `artifacts/v2/generated/openapi.sdk.json`，其中包含原接口文档缺失的明确响应类型，来源为 `http://127.0.0.1:5082/api/v2/openapi/v2.json`。生成的类型已被请求层及认证、设备、媒体、报警、导出、发布等业务模型引用；普通构建不依赖生成工具或 API 在线。

开发代理由 `VITE_API_PROXY_TARGET` 指定，默认目标为现有 HTTPS 服务器，证书正常校验。`VITE_API_BASE` 仅用于确有跨源部署需求的环境；常规部署使用同源 `/api/v2`、`/hubs/v2/events`、`/media`。开发与生产环境都不能关闭证书校验。浏览器认证依赖 Secure、HttpOnly Cookie，需通过 HTTPS 完成登录。反向代理必须为前端路由提供 `index.html` 回退。

## 页面

- `/`：公开客户端下载，显示真实发布状态、更新说明和校验值。
- `/login`：Cookie 与 CSRF 认证。
- `/app`：总览；`live`、`playback`：实时与回放；`alarms`、`exports`：报警与导出。
- `/app/devices`、`organization`、`accounts`：录像机、组织通道、账号角色与范围。
- `/app/layouts`、`sessions`、`audit`、`releases`、`settings`、`profile`：布局轮巡、会话、审计、发布、系统设置与个人资料。

## 接口与生命周期

遵循 `docs/v2-接口协作契约.md`，业务对象使用具名字段，所有媒体与 PTZ 操作使用全局 `channelId`。通道选项与视频树循环读取全部分页，管理表格按页加载。实际角色列表为数组；导出权限为 `export.create`，发布权限为 `desktop.release.manage`，共享轮巡权限为 `layout.share`。

登录前获取 CSRF，登录、退出与刷新后重新获取。并发 401 共享一次续期请求，明确 CSRF 失效返回 400 或 403 时重新取令牌重试一次；权限拒绝不重试。浏览器本地存储不保存登录令牌。

SignalR 使用 Cookie 连接 `/hubs/v2/events`，按资源版本去重，事件与断线恢复均重新查询。权限变更立即停止当前本地播放与 PTZ，重新获取授权通道后才允许恢复合法窗口。设备状态变化只更新相关通道，不重建整个视频墙。

mpegts.js 按需加载，媒体申请使用 `profile: browser`，仅消费经过鉴权的 HTTP-FLV 地址，不回退 HLS。浏览器不支持 MSE（媒体源扩展）时提示使用桌面客户端。媒体每 30 秒续期，回放每 3 秒更新状态。切换、退出、卸载均销毁播放器并释放租约，迟到的启动响应会被立即关闭。失败的停止记录保留并重试。PTZ 每 2 秒续租，松开、失焦、切换、离开页面均停止，启动响应迟到时再次停止。

布局契约使用可空通道数组保存中间空窗口，轮巡方案仍只允许有效通道。共享布局只展示当前可访问通道，服务端负责最终授权。

## 验证边界

`npm test` 验证认证重试、CSRF 身份变化、多设备隔离、分页、轮巡、录像缺口、媒体请求竞态、停止幂等与 PTZ 竞态。测试中的传输桩仅用于独立逻辑验证，产品页面没有模拟业务或伪造成功状态。

真实媒体、H.265、回放定位、抓拍、下载、升级及各视口截图由主实施者在新版候选环境集成验收。旧站界面不能作为新版验收结果。安装包发布与服务器切换由主实施者负责。

品牌统一为 VisiCore（视枢），侧栏按空间分别展示中英文。当前标识与站点图标使用自有原生 SVG `public/visicore.svg`；旧品牌图片不再用于当前界面。公开首页保留 `public/product-workspace.png` 的真实历史截图，显式标注为更名前的界面，仅作功能说明，不作为本次验收结果；读取失败时回退当前品牌标识。MSI 与 ZIP 下载入口使用最新发布响应的 `packages`，缺失时兼容当前单包，未发布的包不会生成虚假下载地址。
