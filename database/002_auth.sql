-- 认证会话和首版功能权限。
create table if not exists sessions (
    token_hash char(64) primary key,
    user_id bigint not null references users(id) on delete cascade,
    expires_at timestamptz not null,
    created_at timestamptz not null default now(),
    revoked_at timestamptz
);

create index if not exists ix_sessions_user on sessions(user_id);
create index if not exists ix_sessions_expiry on sessions(expires_at);

insert into permissions(code, name, resource_type, operation_type) values
    ('user.read', '查看账号', 'user', 'read'),
    ('device.read', '查看设备', 'device', 'read'),
    ('device.manage', '管理设备', 'device', 'manage'),
    ('channel.read', '查看通道', 'channel', 'read'),
    ('channel.assign', '分配通道', 'channel', 'assign'),
    ('live.view', '实时预览', 'live', 'view'),
    ('playback.view', '录像回放', 'playback', 'view'),
    ('ptz.control', '云台控制', 'ptz', 'control'),
    ('alarm.read', '查看报警', 'alarm', 'read'),
    ('alarm.ack', '确认报警', 'alarm', 'ack'),
    ('area.read', '查看业务区域', 'area', 'read'),
    ('user.manage', '管理账号', 'user', 'manage'),
    ('role.manage', '管理角色', 'role', 'manage'),
    ('role.read', '查看角色', 'role', 'read'),
    ('area.manage', '管理业务区域', 'area', 'manage'),
    ('statistics.read', '查看系统统计', 'statistics', 'read')
on conflict (code) do nothing;

insert into roles(name, code) values ('系统管理员', 'admin') on conflict (code) do nothing;
insert into roles(name, code) values ('值班员', 'operator') on conflict (code) do nothing;
insert into role_permissions(role_id, permission_code)
select r.id, p.code from roles r cross join permissions p
where r.code = 'admin' on conflict do nothing;
insert into role_permissions(role_id, permission_code)
select r.id, p.code from roles r join permissions p on p.code in ('device.read', 'channel.read', 'live.view', 'alarm.read', 'area.read', 'statistics.read')
where r.code = 'operator' on conflict do nothing;
