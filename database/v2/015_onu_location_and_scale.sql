-- 015_onu_location_and_scale.sql
-- 扩展 ONU 位置维度（机框、槽位、PON 端口），修复 ONU ID 在不同 PON 口碰撞导致数量截断问题

alter table onu_devices add column if not exists frame_id integer not null default 0;
alter table onu_devices add column if not exists slot_id integer not null default 1;
alter table onu_devices add column if not exists port_id integer not null default 0;
alter table onu_devices add column if not exists description varchar(256);
alter table onu_devices add column if not exists group_name varchar(128);
alter table onu_devices add column if not exists full_dn varchar(128);
alter table onu_devices add column if not exists ip varchar(64);

-- 根据已存 onu_name 更新位置字段 (例如 OLT/0/1/5/35)
update onu_devices 
set 
    frame_id = coalesce(nullif(split_part(onu_name, '/', 2), '')::int, 0),
    slot_id = coalesce(nullif(split_part(onu_name, '/', 3), '')::int, 1),
    port_id = coalesce(nullif(split_part(onu_name, '/', 4), '')::int, 0)
where onu_name like '%/%/%/%/%';

-- 删除旧的 OLT+ONU_ID 联合唯一约束，改为 OLT+机框+槽位+端口+ONU_ID 全局物理拓扑唯一
alter table onu_devices drop constraint if exists uk_onu_devices_nedn_onuid;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'uk_onu_devices_location') then
        alter table onu_devices add constraint uk_onu_devices_location unique(ne_dn, frame_id, slot_id, port_id, onu_id);
    end if;
end $$;

create index if not exists ix_onu_devices_location on onu_devices(ne_dn, frame_id, slot_id, port_id, onu_id);
create index if not exists ix_onu_devices_group on onu_devices(group_name);
