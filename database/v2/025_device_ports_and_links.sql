-- 025_device_ports_and_links.sql
-- 支持设备物理端口（电口/光口/PON/Wi-Fi/电话POTS接口）、点对点跳线链路（Device Links）与宿主机虚拟机（VMs）资产纳管

-- 1. 物理端口表（多态关联：network_device, server, onu_device, computer 等）
create table if not exists device_ports (
    id bigserial primary key,
    device_id bigint not null,
    device_type varchar(32) not null default 'network_device', -- network_device, server, onu_device, computer
    port_name varchar(64) not null,
    port_type varchar(32) not null default 'copper', -- copper(电口), fiber(光口), pon(PON口), wifi, pots(电话语音口), mgmt(带外)
    speed varchar(32) not null default '1Gbps', -- 100M, 1Gbps, 2.5Gbps, 10Gbps, 40Gbps, 100Gbps, other
    status varchar(32) not null default 'unused', -- up, down, unused, disabled
    slot_number integer default 0,
    port_index integer not null default 1,
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uk_device_port unique(device_type, device_id, port_name)
);
create index if not exists ix_device_ports_dev on device_ports(device_type, device_id);
create index if not exists ix_device_ports_type on device_ports(port_type);

-- 2. 点对点跳线链路表
create table if not exists device_links (
    id bigserial primary key,
    local_port_id bigint not null references device_ports(id) on delete cascade,
    peer_port_id bigint not null references device_ports(id) on delete cascade,
    cable_type varchar(32) not null default 'copper', -- copper(双绞线), single_fiber(单模光纤), multi_fiber(多模光纤), dac(直连铜缆), telephone_line(电话线)
    cable_label varchar(64), -- 现场跳线线标 (如 LK-CORE-01-SW02-P25)
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uk_device_links_local unique(local_port_id),
    constraint uk_device_links_peer unique(peer_port_id),
    constraint ck_device_links_no_self check(local_port_id <> peer_port_id)
);
create index if not exists ix_device_links_local on device_links(local_port_id);
create index if not exists ix_device_links_peer on device_links(peer_port_id);

-- 3. 宿主机虚拟机从属分配表
create table if not exists virtual_machines (
    id bigserial primary key,
    host_device_id bigint not null references network_devices(id) on delete cascade,
    vm_name varchar(128) not null,
    ip varchar(64) not null,
    vlan_id integer,
    os varchar(64),
    cpu_cores integer default 4,
    memory_gb numeric(8,2) default 8.0,
    status varchar(32) not null default 'running', -- running, stopped
    business_desc text, -- 业务说明，例如 'NeoSight全光网管服务', 'MES应用数据库'
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_vms_host on virtual_machines(host_device_id);
create index if not exists ix_vms_ip on virtual_machines(ip);

-- 4. 注册与授权新权限
insert into permissions(code, name) values
    ('device_port.manage', '管理设备物理端口与跳线'),
    ('virtual_machine.manage', '管理物理机虚拟机')
on conflict (code) do update set name = excluded.name;

insert into role_permissions(role_id, permission_code)
select id, 'device_port.manage' from roles where code in ('admin', 'operator')
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'virtual_machine.manage' from roles where code in ('admin', 'operator')
on conflict do nothing;
