create table if not exists device_plugins (
    id varchar(64) primary key,
    name varchar(128) not null,
    vendor varchar(64) not null,
    version varchar(32) not null,
    description varchar(512),
    status text not null default 'active' check (status in ('active','disabled')),
    endpoint_url varchar(256) not null,
    capabilities jsonb not null default '[]',
    config_schema jsonb not null default '{}',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

alter table devices add column if not exists plugin_id varchar(64) references device_plugins(id);
alter table devices add column if not exists extra_config jsonb not null default '{}';
create index if not exists ix_devices_plugin on devices(plugin_id);

insert into device_plugins(id, name, vendor, version, description, status, endpoint_url, capabilities, config_schema)
values(
    'hikvision',
    '海康威视网络设备驱动',
    'Hikvision',
    '2.0.0',
    '支持海康威视全系列网络摄像机（IPC）、嵌入式录像机（NVR/DVR）及存储服务器，支持 ISAPI 与 HCNetSDK 混合协议，纯透传零转码。',
    'active',
    'http://127.0.0.1:5092',
    '["live","playback","recordings","ptz","alarms","presets"]'::jsonb,
    '{"type":"object","properties":{}}'::jsonb
) on conflict (id) do update set
    name=excluded.name, vendor=excluded.vendor, version=excluded.version,
    description=excluded.description, endpoint_url=excluded.endpoint_url,
    capabilities=excluded.capabilities, updated_at=now();

update devices set plugin_id='hikvision' where plugin_id is null;
