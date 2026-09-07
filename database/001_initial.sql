-- 视频平台首版核心数据模型。设备和通道保留历史并停用；业务节点按约束物理删除。
create table if not exists users (
    id bigserial primary key,
    username varchar(64) not null unique,
    password_hash varchar(256) not null,
    status varchar(16) not null default 'active' check (status in ('active', 'disabled', 'locked')),
    last_login_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists roles (
    id bigserial primary key,
    name varchar(64) not null,
    code varchar(64) not null unique,
    status varchar(16) not null default 'active' check (status in ('active', 'disabled'))
);

create table if not exists permissions (
    code varchar(64) primary key,
    name varchar(128) not null,
    resource_type varchar(32) not null,
    operation_type varchar(32) not null
);

create table if not exists user_roles (
    user_id bigint not null references users(id),
    role_id bigint not null references roles(id),
    primary key (user_id, role_id)
);

create table if not exists role_permissions (
    role_id bigint not null references roles(id),
    permission_code varchar(64) not null references permissions(code),
    primary key (role_id, permission_code)
);

create table if not exists workshops (
    id bigserial primary key,
    name varchar(128) not null,
    code varchar(64) not null unique,
    status varchar(16) not null default 'active' check (status in ('active', 'disabled'))
);

create table if not exists areas (
    id bigserial primary key,
    workshop_id bigint not null references workshops(id),
    name varchar(128) not null,
    code varchar(64) not null,
    status varchar(16) not null default 'active' check (status in ('active', 'disabled')),
    unique (workshop_id, code)
);

create table if not exists units (
    id bigserial primary key,
    area_id bigint not null references areas(id),
    name varchar(128) not null,
    code varchar(64) not null,
    status varchar(16) not null default 'active' check (status in ('active', 'disabled')),
    unique (area_id, code)
);

create table if not exists devices (
    id bigserial primary key,
    device_key varchar(128) not null unique,
    ip inet not null,
    service_port integer not null default 8000 check (service_port between 1 and 65535),
    model varchar(128),
    serial_number varchar(128),
    status varchar(16) not null default 'unknown' check (status in ('online', 'offline', 'unknown', 'disabled')),
    last_seen_at timestamptz
);

create table if not exists channels (
    id bigserial primary key,
    device_id bigint not null references devices(id),
    device_channel integer not null check (device_channel > 0),
    name varchar(256),
    model varchar(128),
    unit_id bigint references units(id),
    ptz_capable boolean not null default false,
    status varchar(16) not null default 'unknown' check (status in ('online', 'offline', 'unknown', 'disabled')),
    unique (device_id, device_channel)
);

create table if not exists alarm_events (
    id bigserial primary key,
    device_id bigint references devices(id),
    channel_id bigint references channels(id),
    event_type varchar(128) not null,
    occurred_at timestamptz not null,
    state varchar(16) not null default 'new' check (state in ('new', 'acknowledged', 'resolved')),
    normalized_payload jsonb not null default '{}'::jsonb,
    raw_payload bytea,
    acknowledged_by bigint references users(id),
    acknowledged_at timestamptz,
    handling_note varchar(1024)
);

create table if not exists audit_logs (
    id bigserial primary key,
    user_id bigint references users(id),
    action varchar(128) not null,
    resource varchar(128) not null,
    parameter_summary varchar(2048),
    client_ip inet,
    created_at timestamptz not null default now()
);

create index if not exists ix_channels_unit on channels(unit_id);
create index if not exists ix_alarm_events_time on alarm_events(occurred_at desc);
create index if not exists ix_audit_logs_time on audit_logs(created_at desc);
