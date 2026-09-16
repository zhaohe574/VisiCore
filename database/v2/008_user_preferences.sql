-- 007_layout_presets 之后的增量迁移：新增客户端显示偏好持久化。
--
-- 背景：桌面端 2.1.0 的显示与性能开关（主题、分屏优先子码流、硬件解码、网络缓存、
-- 诊断浮层）此前只保存在本机 desktop-v2.json，换机器值守需要重新配置。
-- 本迁移把这些偏好按账号保存到平台，桌面端登录后加载、修改后回写；本机文件降级为离线兜底。

create table if not exists user_preferences (
    user_id bigint primary key references users(id) on delete cascade,
    theme text not null default 'light' check (theme in ('light', 'dark')),
    prefer_sub_stream_in_grid boolean not null default true,
    hardware_decoding boolean not null default true,
    network_caching_ms int not null default 800 check (network_caching_ms between 200 and 5000),
    show_diagnostics boolean not null default false,
    updated_at timestamptz not null default now()
);

comment on table user_preferences is
    '桌面端显示与性能偏好，按账号保存；仅保存显示层开关，不涉及权限或媒体配额。';
