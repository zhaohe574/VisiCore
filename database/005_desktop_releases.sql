-- Windows 桌面端安装包及发布状态。
create table if not exists desktop_releases (
    id bigserial primary key,
    version varchar(32) not null,
    file_name varchar(255) not null,
    storage_name varchar(128) not null unique,
    sha256 char(64) not null,
    file_size bigint not null check (file_size > 0),
    release_notes varchar(4096) not null default '',
    minimum_version varchar(32),
    force_update boolean not null default false,
    status varchar(16) not null default 'draft' check (status in ('draft', 'published', 'revoked')),
    download_count bigint not null default 0,
    published_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create unique index if not exists ux_desktop_releases_version on desktop_releases(version);
create index if not exists ix_desktop_releases_status on desktop_releases(status, published_at desc);

insert into permissions(code, name, resource_type, operation_type)
values ('desktop.release.manage', '管理桌面端版本', 'desktop_release', 'manage')
on conflict (code) do nothing;
insert into role_permissions(role_id, permission_code)
select r.id, 'desktop.release.manage' from roles r where r.code = 'admin'
on conflict do nothing;
