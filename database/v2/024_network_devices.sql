-- 024_network_devices.sql
-- 支持核心交换机、汇聚交换机、路由器、防火墙等多 IP / 多 VLAN 网关网络管理设备台账与接口分配

-- 1. 网络管理设备表
create table if not exists network_devices (
    id bigserial primary key,
    name varchar(128) not null,
    device_type varchar(64) not null default 'core_switch',
    vendor varchar(64),
    model varchar(128),
    management_ip varchar(64),
    mac varchar(64),
    location varchar(256),
    workshop_id bigint references workshops(id) on delete set null,
    area_id bigint references areas(id) on delete set null,
    owner varchar(128),
    status varchar(32) not null default 'online',
    response_time_ms integer,
    last_probed_at timestamptz,
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_network_devices_type on network_devices(device_type);
create index if not exists ix_network_devices_mgmt_ip on network_devices(management_ip);

-- 2. 网络设备多 IP / 多 VLAN 接口分配表
create table if not exists network_device_ips (
    id bigserial primary key,
    device_id bigint not null references network_devices(id) on delete cascade,
    ip varchar(64) not null,
    vlan_id integer,
    interface_name varchar(64) not null default 'Vlanif',
    role varchar(32) not null default 'gateway',
    description text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uk_network_device_ip unique(device_id, ip)
);
create index if not exists ix_network_device_ips_ip on network_device_ips(ip);
create index if not exists ix_network_device_ips_device on network_device_ips(device_id);

-- 3. 注册权限与角色赋权
insert into permissions(code, name) values
    ('network_device.read', '查看网络设备'),
    ('network_device.manage', '管理网络设备')
on conflict (code) do update set name = excluded.name;

insert into role_permissions(role_id, permission_code)
select id, 'network_device.manage' from roles where code = 'admin'
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'network_device.read' from roles where code = 'operator'
on conflict do nothing;

-- 4. 预置现场核心交换机及其各 VLAN 网关接口示范数据
insert into network_devices (id, name, device_type, vendor, model, management_ip, location, owner, remarks)
values (
    1,
    '总控核心交换机',
    'core_switch',
    'Huawei',
    'CloudEngine S6730-H48X6C',
    '10.37.200.250',
    '总控机房核心机架 A01',
    '网络管理员',
    '全厂核心三层交换机，承载全网各 VLAN 网关路由与汇聚接入'
) on conflict (id) do nothing;

insert into network_device_ips (device_id, ip, vlan_id, interface_name, role, description)
values
    (1, '10.37.20.1', 20, 'Vlanif20', 'gateway', '办公楼设备网段网关'),
    (1, '10.37.24.1', 24, 'Vlanif24', 'gateway', '中维录像机网段网关'),
    (1, '10.37.5.1', 5, 'Vlanif5', 'gateway', 'POL全光网管段网关'),
    (1, '10.37.200.1', 200, 'Vlanif200', 'gateway', '核心服务与存储网段网关')
on conflict (device_id, ip) do nothing;

select setval('network_devices_id_seq', (select coalesce(max(id), 1) from network_devices));
