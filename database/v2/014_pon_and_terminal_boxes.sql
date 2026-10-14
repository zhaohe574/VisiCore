-- 014_pon_and_terminal_boxes.sql
-- 支持华为 eSight POL 光网资产管理、终端箱台账、下挂交换机以及通道网络承载关系绑定

-- 1. 终端箱台账表
create table if not exists terminal_boxes (
    id bigserial primary key,
    code varchar(64) not null unique,
    name varchar(128) not null,
    location varchar(256),
    unit_id bigint references units(id) on delete set null,
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_terminal_boxes_unit on terminal_boxes(unit_id);

-- 2. ONU 资产表（从 eSight 网管定时同步与本地扩充维护）
create table if not exists onu_devices (
    id bigserial primary key,
    ne_dn varchar(128) not null,
    olt_name varchar(128) not null default 'OLT',
    olt_ip varchar(64) not null default '10.37.5.3',
    onu_id varchar(32) not null,
    onu_name varchar(128) not null,
    alias varchar(128),
    sn varchar(64) not null,
    mac varchar(64),
    terminal_type varchar(64) not null default 'EG8040C-10',
    status varchar(32) not null default 'offline',
    run_status varchar(32),
    last_up_time timestamptz,
    last_down_time timestamptz,
    last_down_cause text,
    rx_power numeric(8,2),
    tx_power numeric(8,2),
    terminal_box_id bigint references terminal_boxes(id) on delete set null,
    has_sub_switch boolean not null default false,
    sub_switch_model varchar(128),
    sub_switch_ports integer not null default 0,
    ports_config jsonb not null default '[]'::jsonb,
    last_synced_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uk_onu_devices_nedn_onuid unique(ne_dn, onu_id)
);
create index if not exists ix_onu_devices_sn on onu_devices(sn);
create index if not exists ix_onu_devices_box on onu_devices(terminal_box_id);
create index if not exists ix_onu_devices_status on onu_devices(status);

-- 3. 通道与网络物理链路承载关系表（三级拓扑：终端箱 -> ONU/交换机 -> 端口 -> 摄像头通道）
create table if not exists channel_network_bindings (
    channel_id bigint primary key references channels(id) on delete cascade,
    terminal_box_id bigint references terminal_boxes(id) on delete set null,
    onu_device_id bigint references onu_devices(id) on delete set null,
    port_number integer not null default 1,
    is_switch_port boolean not null default false,
    port_label varchar(64),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_channel_bindings_box on channel_network_bindings(terminal_box_id);
create index if not exists ix_channel_bindings_onu on channel_network_bindings(onu_device_id);

-- 4. 注册华为 eSight POL 光网络设备驱动插件
insert into device_plugins (
    id, name, vendor, version, description, status, endpoint_url, capabilities, config_schema
) values (
    'esight_pon',
    '华为 eSight POL 光网络驱动',
    'Huawei',
    '1.0.0',
    '支持华为 eSight / NeoSight 全光园区网络管理平台，实现 OLT/ONU 光网资产管理、状态监控、光衰诊断、配置同步及免开箱远程重启。',
    'active',
    'https://10.37.5.2:31943',
    '["onu.query","onu.diag","onu.config","onu.reboot","terminal_box.manage"]'::jsonb,
    '{"type":"object","properties":{"host":{"type":"string","default":"10.37.5.2"},"port":{"type":"integer","default":31943},"username":{"type":"string","default":"admin"}}}'::jsonb
) on conflict (id) do update set
    name=excluded.name, vendor=excluded.vendor, version=excluded.version,
    description=excluded.description, endpoint_url=excluded.endpoint_url,
    capabilities=excluded.capabilities, config_schema=excluded.config_schema,
    updated_at=now();

-- 5. 权限注册
insert into permissions(code, name) values
    ('pon.read', '查看光网与终端箱'),
    ('pon.manage', '管理光网与终端箱')
on conflict (code) do update set name=excluded.name;

insert into role_permissions(role_id, permission_code)
select id, 'pon.manage' from roles where code='admin'
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'pon.read' from roles where code='operator'
on conflict do nothing;
