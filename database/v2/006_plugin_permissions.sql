insert into permissions (code, name) values
    ('plugin.read', '查看驱动插件'),
    ('plugin.manage', '管理驱动插件')
on conflict (code) do update set name = excluded.name;

insert into role_permissions (role_id, permission_code)
select r.id, p.code
from roles r
cross join (values ('plugin.read'), ('plugin.manage')) as p(code)
where r.code = 'admin'
on conflict do nothing;
