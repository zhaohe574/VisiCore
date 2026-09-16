-- 增加 25 路分屏权限与布局档位，同步调整每用户实时预览路数

insert into permissions (code, name) values
    ('live.split25', '25路分屏预览')
on conflict (code) do update set name = excluded.name;

insert into role_permissions (role_id, permission_code)
select r.id, 'live.split25'
from roles r
where r.code = 'admin'
on conflict do nothing;

alter table layouts drop constraint if exists layouts_layout_check;

alter table layouts add constraint layouts_layout_check
    check (layout in (1, 4, 6, 8, 9, 10, 16, 25));

comment on column layouts.layout is
    '分屏格数：1／4／9／16／25 为等分档位，8（1+7）与 10（1+9）为一大屏加多小屏的聚焦档位（兼容历史 6）。';

-- 同步升级系统设置中每用户实时预览路数，如果原设置小于 25 则同步提升至 25，避免与 25 路分屏冲突
update settings
set value = jsonb_set(value, '{livePerUser}', '25'::jsonb)
where id = 1 and (value->>'livePerUser')::int < 25;
