-- 026_digital_twin_map.sql
-- 全厂网络与空间数字孪生：2.5D 科技微立体空间底座、空间资产标定与光网脉冲流光链路

-- 1. 数字孪生场景表
create table if not exists map_scenes (
    id bigserial primary key,
    scene_code varchar(64) not null unique, -- factory_overview, pipe_making, hot_dip, spiral, peripheral
    name varchar(128) not null,
    level_type varchar(32) not null default 'l2_workshop', -- l1_overview, l2_workshop
    workshop_id bigint references workshops(id) on delete set null,
    bg_image_url varchar(256) not null,
    natural_width integer not null default 3840,
    natural_height integer not null default 2160,
    default_zoom numeric(4,2) not null default 1.0,
    min_zoom numeric(4,2) not null default 0.4,
    max_zoom numeric(4,2) not null default 5.0,
    center_x_pct numeric(6,4) not null default 0.5000,
    center_y_pct numeric(6,4) not null default 0.5000,
    sort_order integer not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_map_scenes_code on map_scenes(scene_code);
create index if not exists ix_map_scenes_ws on map_scenes(workshop_id);

-- 2. 全资产空间标定表（终端箱、网络核心、工位电脑、监控摄像头）
create table if not exists map_coordinates (
    id bigserial primary key,
    scene_id bigint not null references map_scenes(id) on delete cascade,
    target_type varchar(32) not null, -- terminal_box, network_device, channel, computer
    target_id bigint not null,
    x_pct numeric(6,4) not null, -- 0.0000 ~ 1.0000 归一化横坐标
    y_pct numeric(6,4) not null, -- 0.0000 ~ 1.0000 归一化纵坐标
    altitude_m numeric(6,2) default 1.5, -- 安装高度（米）
    fov_yaw numeric(5,1) default 0.0, -- 摄像机水平指向 (0~360度)
    fov_angle numeric(5,1) default 60.0, -- 监控覆盖开角 (度)
    fov_radius_m numeric(6,2) default 25.0, -- 覆盖范围半径 (米)
    icon_style varchar(64), -- 自定义样式/图标
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    constraint uk_map_coord_target unique(scene_id, target_type, target_id)
);
create index if not exists ix_map_coord_scene on map_coordinates(scene_id);
create index if not exists ix_map_coord_target on map_coordinates(target_type, target_id);

-- 3. 主干光缆与走线拓扑路径表
create table if not exists map_cable_paths (
    id bigserial primary key,
    scene_id bigint not null references map_scenes(id) on delete cascade,
    name varchar(128) not null,
    cable_type varchar(32) not null default 'fiber_core', -- fiber_core, copper_trunk
    from_device_type varchar(32),
    from_device_id bigint,
    to_device_type varchar(32),
    to_device_id bigint,
    color varchar(32) default '#38bdf8',
    points_json jsonb not null default '[]'::jsonb,
    status varchar(32) not null default 'normal', -- normal, degraded, down
    remarks text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);
create index if not exists ix_map_cables_scene on map_cable_paths(scene_id);

-- 4. 场景初始化种子数据
insert into map_scenes (scene_code, name, level_type, workshop_id, bg_image_url, natural_width, natural_height, default_zoom, center_x_pct, center_y_pct, sort_order)
values
    ('factory_overview', '全厂宏观总览', 'l1_overview', null, '/maps/factory_overview.svg', 3840, 2160, 1.0, 0.5000, 0.5000, 0),
    ('pipe_making', '制管车间', 'l2_workshop', (select id from workshops where code='HG' limit 1), '/maps/pipe_making_isometric.svg', 3840, 2160, 1.0, 0.5000, 0.5000, 10),
    ('hot_dip', '热镀车间', 'l2_workshop', (select id from workshops where code='RD' limit 1), '/maps/factory_overview.svg', 3840, 2160, 1.0, 0.5000, 0.5000, 20),
    ('spiral', '螺旋车间', 'l2_workshop', (select id from workshops where code='LX' limit 1), '/maps/factory_overview.svg', 3840, 2160, 1.0, 0.5000, 0.5000, 30),
    ('peripheral', '外围区域', 'l2_workshop', (select id from workshops where code='YW' limit 1), '/maps/factory_overview.svg', 3840, 2160, 1.0, 0.5000, 0.5000, 40)
on conflict (scene_code) do update set
    name = excluded.name,
    level_type = excluded.level_type,
    workshop_id = excluded.workshop_id,
    bg_image_url = excluded.bg_image_url,
    updated_at = now();

-- 5. 初始化全厂宏观主干光缆脉冲路径
insert into map_cable_paths (scene_id, name, cable_type, color, points_json, remarks)
select s.id, '骨干环网-总控机房至制管车间主缆', 'fiber_core', '#38bdf8',
    '[{"x": 0.23, "y": 0.76}, {"x": 0.32, "y": 0.68}, {"x": 0.44, "y": 0.58}, {"x": 0.51, "y": 0.46}]'::jsonb,
    '总控机房核心交换机直达制管车间骨干光缆'
from map_scenes s where s.scene_code = 'factory_overview'
on conflict do nothing;

insert into map_cable_paths (scene_id, name, cable_type, color, points_json, remarks)
select s.id, '骨干环网-总控机房至热镀车间主缆', 'fiber_core', '#06b6d4',
    '[{"x": 0.23, "y": 0.76}, {"x": 0.38, "y": 0.78}, {"x": 0.62, "y": 0.71}, {"x": 0.73, "y": 0.60}]'::jsonb,
    '总控机房核心交换机直达热镀车间骨干光缆'
from map_scenes s where s.scene_code = 'factory_overview'
on conflict do nothing;

insert into map_cable_paths (scene_id, name, cable_type, color, points_json, remarks)
select s.id, '骨干环网-总控机房至螺旋车间主缆', 'fiber_core', '#818cf8',
    '[{"x": 0.23, "y": 0.76}, {"x": 0.26, "y": 0.60}, {"x": 0.30, "y": 0.42}, {"x": 0.36, "y": 0.31}]'::jsonb,
    '总控机房核心交换机直达螺旋车间骨干光缆'
from map_scenes s where s.scene_code = 'factory_overview'
on conflict do nothing;

-- 6. 注册与赋权权限
insert into permissions(code, name) values
    ('map.read', '查看数字孪生大屏'),
    ('map.manage', '管理数字孪生空间标定与光缆')
on conflict (code) do update set name = excluded.name;

insert into role_permissions(role_id, permission_code)
select id, 'map.read' from roles where code in ('admin', 'operator')
on conflict do nothing;

insert into role_permissions(role_id, permission_code)
select id, 'map.manage' from roles where code in ('admin', 'operator')
on conflict do nothing;
