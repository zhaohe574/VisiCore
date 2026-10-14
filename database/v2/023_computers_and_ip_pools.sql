-- 023_computers_and_ip_pools.sql
-- 支持电脑设备台账管理（含组织归属、物理位置与 ONU 拓扑端口绑定）、IP 池管理与网络扫描探测缓存

-- 1. 电脑台账表
create table if not exists computers (
    id bigserial primary key,
    name varchar(128) not null,
    ip varchar(64) not null,
    mac varchar(64),
    owner varchar(128),
    department varchar(128),
    os varchar(64),
    location varchar(256),
    workshop_id bigint references workshops(id) on delete set null,
    area_id bigint references areas(id) on delete set null,
    unit_id bigint references units(id) on delete set null,
    terminal_box_id bigint references terminal_boxes(id) on delete set null,
    onu_device_id bigint references onu_devices(id) on delete set null,
    port_number integer not null default 1,
    is_switch_port boolean not null default false,
    port_label varchar(64),
    status varchar(32) not null default 'unknown',
    response_time_ms integer,
    last_seen_at timestamptz,
    last_probed_at timestamptz,
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_computers_ip on computers(ip);
create index if not exists ix_computers_mac on computers(mac);
create index if not exists ix_computers_area on computers(area_id);
create index if not exists ix_computers_onu on computers(onu_device_id);

-- 2. IP 池号段定义表（支持 /22, /24 等大网段与任意 CIDR）
create table if not exists ip_pools (
    id bigserial primary key,
    name varchar(128) not null,
    cidr varchar(64) not null unique,
    start_ip varchar(64) not null,
    end_ip varchar(64) not null,
    gateway varchar(64),
    netmask varchar(64) not null default '255.255.255.0',
    vlan_id integer,
    description text,
    workshop_id bigint references workshops(id) on delete set null,
    area_id bigint references areas(id) on delete set null,
    auto_scan boolean not null default true,
    last_scanned_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_ip_pools_cidr on ip_pools(cidr);

-- 3. IP 扫描与活跃探测缓存记录表
create table if not exists ip_scan_records (
    id bigserial primary key,
    pool_id bigint not null references ip_pools(id) on delete cascade,
    ip varchar(64) not null,
    is_active boolean not null default false,
    response_time_ms integer,
    last_probed_at timestamptz not null default now(),
    constraint uk_ip_scan_pool_ip unique(pool_id, ip)
);
create index if not exists ix_ip_scan_pool on ip_scan_records(pool_id);
create index if not exists ix_ip_scan_ip on ip_scan_records(ip);

-- 4. 权限注册与角色赋权
insert into permissions(code, name) values
    ('computer.read', '查看电脑台账'),
    ('computer.manage', '管理电脑台账'),
    ('ip.read', '查看IP池'),
    ('ip.manage', '管理IP池')
on conflict (code) do update set name=excluded.name;

insert into role_permissions(role_id, permission_code)
select id, 'computer.manage' from roles where code='admin'
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'ip.manage' from roles where code='admin'
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'computer.read' from roles where code='operator'
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'ip.read' from roles where code='operator'
on conflict do nothing;

-- 5. 预置现场常见已知网段（方便即开即用）
insert into ip_pools (name, cidr, start_ip, end_ip, gateway, netmask, vlan_id, description)
values
    ('办公楼设备网段', '10.37.20.0/24', '10.37.20.1', '10.37.20.254', '10.37.20.1', '255.255.255.0', 20, '办公楼接入交换机与IPC摄像机网段'),
    ('中维录像机网段', '10.37.24.0/24', '10.37.24.1', '10.37.24.254', '10.37.24.1', '255.255.255.0', 24, '中维录像机与关联设备网段'),
    ('POL全光网管段', '10.37.5.0/24', '10.37.5.1', '10.37.5.254', '10.37.5.1', '255.255.255.0', 5, '华为OLT及NeoSight网管网段'),
    ('核心服务与存储网段', '10.37.200.0/24', '10.37.200.1', '10.37.200.254', '10.37.200.1', '255.255.255.0', 200, '视频平台生产机及集中存储网段')
on conflict (cidr) do nothing;
