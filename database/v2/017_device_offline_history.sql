-- 017_device_offline_history.sql
-- 记录终端箱、ONU 设备与摄像头通道的掉线与恢复历史记录，支持故障排查与运行审计

create table if not exists device_offline_history (
    id bigserial primary key,
    device_type varchar(32) not null check (device_type in ('onu', 'camera')),
    terminal_box_id bigint references terminal_boxes(id) on delete set null,
    terminal_box_code varchar(64),
    terminal_box_name varchar(128),
    onu_device_id bigint references onu_devices(id) on delete set null,
    channel_id bigint references channels(id) on delete set null,
    device_name varchar(256) not null,
    device_identifier varchar(128),
    event_type varchar(32) not null default 'offline' check (event_type in ('offline', 'online')),
    down_time timestamptz not null,
    up_time timestamptz,
    duration_seconds integer,
    reason text,
    created_at timestamptz not null default now()
);

create index if not exists ix_doh_box on device_offline_history(terminal_box_id, down_time desc);
create index if not exists ix_doh_onu on device_offline_history(onu_device_id, down_time desc);
create index if not exists ix_doh_channel on device_offline_history(channel_id, down_time desc);
create index if not exists ix_doh_down_time on device_offline_history(down_time desc);
create index if not exists ix_doh_active on device_offline_history(up_time) where up_time is null;

-- 初始化回填：将当前已处于离线状态且有掉线记录的 ONU 设备纳入历史表，确保系统上线后即可追踪当前持续中的掉线事件
insert into device_offline_history (
    device_type, terminal_box_id, terminal_box_code, terminal_box_name,
    onu_device_id, device_name, device_identifier, event_type,
    down_time, up_time, duration_seconds, reason
)
select 
    'onu' as device_type,
    o.terminal_box_id,
    b.code as terminal_box_code,
    b.name as terminal_box_name,
    o.id as onu_device_id,
    coalesce(nullif(o.alias, ''), o.onu_name) as device_name,
    o.sn as device_identifier,
    'offline' as event_type,
    coalesce(o.last_down_time, now() - interval '1 hour') as down_time,
    null as up_time,
    null as duration_seconds,
    coalesce(o.last_down_cause, '光网离线/掉电') as reason
from onu_devices o
left join terminal_boxes b on o.terminal_box_id = b.id
where not (o.status in ('1', 'online') or o.run_status = '1')
  and not exists (
      select 1 from device_offline_history h 
      where h.onu_device_id = o.id and h.up_time is null
  );
