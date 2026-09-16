-- 008_user_preferences 之后的增量迁移：新增 SSL 管控与网址/域名管理。
--
-- 功能：
-- 1. ssl_certificates：保存上传的 SSL 证书（CRT/PEM、KEY 私钥、主题、SAN 域名列表、有效期、指纹等）。
-- 2. ssl_domains：管理平台的受访网址与域名（HTTP/HTTPS 协议、端口、主访问域名、HSTS、关联证书等）。
-- 3. 新增 ssl.read、ssl.manage 权限，并赋予管理员角色。

create table if not exists ssl_certificates (
    id bigserial primary key,
    name varchar(128) not null,
    cert_pem text not null,
    key_pem text not null,
    subject_dn text not null,
    issuer_dn text not null,
    common_name varchar(255) not null,
    dns_names text[] not null default '{}',
    serial_number varchar(128) not null,
    thumbprint varchar(128) not null,
    valid_from timestamptz not null,
    valid_to timestamptz not null,
    is_active boolean not null default false,
    auto_renew boolean not null default false,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create table if not exists ssl_domains (
    id bigserial primary key,
    domain varchar(255) not null unique,
    port int not null default 443 check (port between 1 and 65535),
    protocol text not null default 'https' check (protocol in ('https', 'http')),
    description varchar(255) not null default '',
    is_primary boolean not null default false,
    force_https boolean not null default true,
    hsts_enabled boolean not null default true,
    certificate_id bigint references ssl_certificates(id) on delete set null,
    enabled boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

-- 索引
create index if not exists ix_ssl_cert_active on ssl_certificates(is_active) where is_active is true;
create index if not exists ix_ssl_domain_primary on ssl_domains(is_primary) where is_primary is true;

-- 权限声明
insert into permissions (code, name) values
    ('ssl.read', '查看SSL与域名'),
    ('ssl.manage', '管理SSL与域名')
on conflict (code) do update set name = excluded.name;

insert into role_permissions (role_id, permission_code)
select r.id, p.code
from roles r
cross join (values ('ssl.read'), ('ssl.manage')) as p(code)
where r.code = 'admin'
on conflict do nothing;
