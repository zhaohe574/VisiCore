-- 角色和用户的数据范围。scope_type 可为 workshop、area、unit、channel。
create table if not exists role_scopes (
    role_id bigint not null references roles(id) on delete cascade,
    scope_type varchar(16) not null check (scope_type in ('workshop', 'area', 'unit', 'channel')),
    scope_id bigint not null check (scope_id > 0),
    primary key (role_id, scope_type, scope_id)
);

create table if not exists user_scopes (
    user_id bigint not null references users(id) on delete cascade,
    scope_type varchar(16) not null check (scope_type in ('workshop', 'area', 'unit', 'channel')),
    scope_id bigint not null check (scope_id > 0),
    primary key (user_id, scope_type, scope_id)
);

create index if not exists ix_role_scopes_lookup on role_scopes(role_id, scope_type, scope_id);
create index if not exists ix_user_scopes_lookup on user_scopes(user_id, scope_type, scope_id);
