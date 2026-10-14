-- 020_pon_olt_and_root_cause.sql
-- 华为 eSight/NeoSight POL 光网全面重构与全业务深度耦合：
-- 1. OLT 核心网元资产表 (olt_devices)
-- 2. onu_devices 扩展五层拓扑（OLT、PON端口if_desc、Dying Gasp掉电遗言、智能根因health_status、30秒防抖状态变迁时间）
-- 3. 全网光链路健康度历史快照表 (optical_health_snapshots)
-- 4. 驱动插件 endpoint_url 升级为官方 OpenAPI 网关 32102

-- 1. OLT 核心网元资产表
create table if not exists olt_devices (
    id bigserial primary key,
    nedn varchar(128) not null unique,
    name varchar(128) not null default 'OLT',
    ip varchar(64) not null default '10.37.5.3',
    model varchar(128) not null default 'EA5801E-GP08-H3',
    vendor varchar(64) not null default 'Huawei',
    os_version varchar(64),
    patch_version varchar(64),
    status varchar(32) not null default 'online',
    synced_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

-- 初始化插入现场 EA5801E 核心 OLT（如已存在则更新）
insert into olt_devices (nedn, name, ip, model, vendor, os_version, patch_version, status, synced_at)
values (
    'a26a9d2a-ac90-4a80-8051-4288d3eb07e1',
    'OLT',
    '10.37.5.3',
    'EA5801E-GP08-H3',
    'Huawei',
    'EA5801EV100R025C10',
    'SPC200',
    'online',
    now()
) on conflict (nedn) do update set
    name = excluded.name,
    ip = excluded.ip,
    model = excluded.model,
    vendor = excluded.vendor,
    os_version = excluded.os_version,
    patch_version = excluded.patch_version,
    status = excluded.status,
    synced_at = excluded.synced_at,
    updated_at = now();

-- 2. onu_devices 扩展字段
alter table onu_devices add column if not exists olt_id bigint references olt_devices(id) on delete set null;
alter table onu_devices add column if not exists port_id integer not null default 0;
alter table onu_devices add column if not exists if_desc varchar(64);
alter table onu_devices add column if not exists last_dying_gasp_time timestamptz;
alter table onu_devices add column if not exists health_status varchar(32) not null default 'healthy';
alter table onu_devices add column if not exists root_cause text;
alter table onu_devices add column if not exists last_state_change_time timestamptz default now();

-- 默认将已存 ONU 关联至上述 OLT
update onu_devices
set olt_id = (select id from olt_devices where nedn = 'a26a9d2a-ac90-4a80-8051-4288d3eb07e1' limit 1)
where olt_id is null;

-- 初始化 if_desc (若存在 GPON 端口编号)
update onu_devices
set if_desc = 'GPON 0/' || coalesce(slot_id, 1)::text || '/' || coalesce(port_id, 0)::text
where if_desc is null or if_desc = '';

-- 初始化现有健康状态与根因定界
update onu_devices
set 
    health_status = case 
        when not (status in ('1', 'online') or run_status = '1') then 
            case 
                when last_dying_gasp_time is not null and last_down_time is not null and abs(extract(epoch from (last_down_time - last_dying_gasp_time))) <= 15 then 'power_off'
                else 'fiber_cut'
            end
        when rx_power is not null and rx_power < -25.0 then 'sub_health_weak_light'
        else 'healthy'
    end,
    root_cause = case
        when not (status in ('1', 'online') or run_status = '1') then 
            case 
                when last_dying_gasp_time is not null and last_down_time is not null and abs(extract(epoch from (last_down_time - last_dying_gasp_time))) <= 15 then '终端箱掉电 / 供电中断 (收到 Dying Gasp 掉电遗言)'
                else '分支光纤断纤 / 链路中断 (Cause: ' || coalesce(last_down_cause, '13') || ')'
            end
        when rx_power is not null and rx_power < -25.0 then '弱光亚健康 (接收光功率: ' || rx_power::text || 'dBm)'
        else null
    end;

create index if not exists ix_onu_devices_olt on onu_devices(olt_id);
create index if not exists ix_onu_devices_if_desc on onu_devices(if_desc);
create index if not exists ix_onu_devices_health on onu_devices(health_status);
create index if not exists ix_onu_devices_dying_gasp on onu_devices(last_dying_gasp_time);

-- 3. 全网光链路健康度历史快照表
create table if not exists optical_health_snapshots (
    id bigserial primary key,
    score integer not null default 100,
    total_onus integer not null default 0,
    online_onus integer not null default 0,
    offline_onus integer not null default 0,
    healthy_onus integer not null default 0,
    sub_health_onus integer not null default 0,
    power_off_onus integer not null default 0,
    fiber_cut_onus integer not null default 0,
    trunk_broken_onus integer not null default 0,
    avg_rx_power numeric(8,2),
    recorded_at timestamptz not null default now()
);

create index if not exists ix_optical_health_snapshots_recorded on optical_health_snapshots(recorded_at desc);

-- 初始写入一条健康基线快照
insert into optical_health_snapshots (
    score, total_onus, online_onus, offline_onus, healthy_onus,
    sub_health_onus, power_off_onus, fiber_cut_onus, trunk_broken_onus,
    avg_rx_power, recorded_at
)
select
    greatest(0, 100 - (count(*) filter (where not (status in ('1', 'online') or run_status = '1')) * 1) - (count(*) filter (where rx_power is not null and rx_power < -25.0) * 1))::int,
    count(*)::int,
    count(*) filter (where status in ('1', 'online') or run_status = '1')::int,
    count(*) filter (where not (status in ('1', 'online') or run_status = '1'))::int,
    count(*) filter (where (status in ('1', 'online') or run_status = '1') and (rx_power is null or rx_power >= -25.0))::int,
    count(*) filter (where (status in ('1', 'online') or run_status = '1') and rx_power is not null and rx_power < -25.0)::int,
    count(*) filter (where not (status in ('1', 'online') or run_status = '1') and health_status = 'power_off')::int,
    count(*) filter (where not (status in ('1', 'online') or run_status = '1') and health_status != 'power_off')::int,
    0,
    round(avg(rx_power) filter (where rx_power is not null), 2),
    now()
from onu_devices;

-- 4. 驱动插件表更新为 32102 OpenAPI
update device_plugins
set endpoint_url = 'https://10.37.5.2:32102',
    version = '2.0.0',
    description = '基于华为 iMaster NeoSight 官方北向 OpenAPI 网关（32102），提供 OLT/ONU 全光资产管理、智能故障根因分析、弱光预警与自动拓扑发现。',
    capabilities = '["olt.query","onu.query","onu.diag","onu.alias","onu.reboot","terminal_box.manage","topology.auto_discover","health.dashboard"]'::jsonb,
    config_schema = '{"type":"object","properties":{"host":{"type":"string","default":"10.37.5.2"},"port":{"type":"integer","default":32102},"username":{"type":"string","default":"visicore"}}}'::jsonb,
    updated_at = now()
where id = 'esight_pon';
