create table roles (
    id bigserial primary key, name varchar(128) not null, code varchar(64) not null unique,
    status text not null default 'active' check (status in ('active','disabled')),
    all_channels boolean not null default false
);
create table users (
    id bigserial primary key, username varchar(64) not null unique, password_hash text not null,
    display_name varchar(128), phone varchar(32), status text not null default 'active' check(status in('active','disabled','locked')),
    all_channels boolean not null default false, failed_logins int not null default 0, locked_until timestamptz,
    last_login_at timestamptz, created_at timestamptz not null default now(), updated_at timestamptz not null default now()
);
create table permissions(code varchar(64) primary key, name varchar(128) not null);
create table user_roles(user_id bigint references users(id), role_id bigint references roles(id), primary key(user_id,role_id));
create table role_permissions(role_id bigint references roles(id) on delete cascade, permission_code varchar(64) references permissions(code), primary key(role_id,permission_code));
create table sessions (
    id uuid primary key, user_id bigint not null references users(id), token_hash char(64) unique not null,
    client_type text not null check(client_type in('web','desktop')), client_version varchar(64) not null,
    client_ip inet, created_at timestamptz not null default now(), last_seen_at timestamptz not null default now(),
    expires_at timestamptz not null, revoked_at timestamptz
);
create index ix_sessions_user on sessions(user_id,expires_at) where revoked_at is null;
create table workshops(id bigserial primary key,name varchar(128) not null,code varchar(64) not null unique,status text not null default 'active' check(status in('active','disabled')));
create table areas(id bigserial primary key,parent_id bigint not null references workshops(id),name varchar(128) not null,code varchar(64) not null,status text not null default 'active' check(status in('active','disabled')),unique(parent_id,code));
create table units(id bigserial primary key,parent_id bigint not null references areas(id),name varchar(128) not null,code varchar(64) not null,status text not null default 'active' check(status in('active','disabled')),unique(parent_id,code));
create table devices (
    id bigserial primary key,name varchar(128) not null,host varchar(253) not null,port int not null check(port between 1 and 65535),
    username varchar(128) not null,password_cipher text not null,enabled boolean not null default true,
    status text not null default 'unknown',model varchar(128),serial_number varchar(256),last_seen_at timestamptz,
    sync_error text, created_at timestamptz not null default now(),unique(host,port)
);
create table channels (
    id bigserial primary key,device_id bigint not null references devices(id),device_channel int not null check(device_channel>0),
    name varchar(256) not null,model varchar(128),status text not null default 'unknown',unit_id bigint references units(id),
    ptz_capable boolean not null default false,codec varchar(32),updated_at timestamptz not null default now(),unique(device_id,device_channel)
);
create index ix_channels_unit on channels(unit_id);
create table user_scopes(user_id bigint references users(id) on delete cascade,type text not null check(type in('workshop','area','unit','channel')),scope_id bigint not null check(scope_id>0),primary key(user_id,type,scope_id));
create table role_scopes(role_id bigint references roles(id) on delete cascade,type text not null check(type in('workshop','area','unit','channel')),scope_id bigint not null check(scope_id>0),primary key(role_id,type,scope_id));
create table media_sessions (
    id uuid primary key,user_id bigint not null references users(id),auth_session_id uuid not null references sessions(id),
    device_id bigint not null references devices(id),channel_id bigint not null references channels(id),kind text not null check(kind in('live','playback')),
    stream_type int not null default 2,profile text not null,stream text,token_hash char(64) unique not null,token_cipher text not null,
    state text not null default 'starting',transcoded boolean not null default false,start_at timestamptz,end_at timestamptz,
    created_at timestamptz not null default now(),expires_at timestamptz not null,closed_at timestamptz,error text
);
create index ix_media_active on media_sessions(user_id,device_id,kind) where closed_at is null;
create table viewer_connections(connection_id text primary key,media_session_id uuid not null references media_sessions(id),created_at timestamptz not null default now());
create table ptz_leases(channel_id bigint primary key references channels(id),user_id bigint not null references users(id),auth_session_id uuid not null references sessions(id),command text not null,speed int not null,expires_at timestamptz not null);
create table favorites(user_id bigint references users(id),channel_id bigint references channels(id),sort_order int not null,primary key(user_id,channel_id));
create table layouts(id bigserial primary key,user_id bigint not null references users(id),name varchar(128) not null,kind text not null check(kind in('layout','patrol')),shared boolean not null default false,layout int not null check(layout in(1,4,9,16)),interval_seconds int not null check(interval_seconds between 10 and 300),channel_ids bigint[] not null,updated_at timestamptz not null default now());
create table alarm_events (
    id bigserial primary key,device_id bigint not null references devices(id),channel_id bigint references channels(id),
    source_id text not null,event_type varchar(128) not null,occurred_at timestamptz not null,
    state text not null default 'new' check(state in('new','processing','closed')),recovered boolean not null default false,
    recovery_of bigint references alarm_events(id),owner_id bigint references users(id),note varchar(4096) not null default '',
    payload jsonb not null default '{}',image_payload bytea,version bigint not null default 1,unique(device_id,source_id)
);
create index ix_alarms_time on alarm_events(occurred_at desc,id desc);
create index ix_alarms_scope on alarm_events(channel_id,state,occurred_at desc);
create table alarm_history(id bigserial primary key,alarm_id bigint not null references alarm_events(id) on delete cascade,user_id bigint references users(id),action text not null,note varchar(4096) not null default '',created_at timestamptz not null default now());
create table adapter_checkpoints(device_id bigint primary key references devices(id),cursor text not null default '',updated_at timestamptz not null default now());
create table export_jobs (
    id uuid primary key,user_id bigint not null references users(id),auth_session_id uuid not null references sessions(id),channel_id bigint not null references channels(id),device_id bigint not null references devices(id),
    start_at timestamptz not null,end_at timestamptz not null,state text not null default 'queued' check(state in('queued','running','completed','failed','cancelled')),
    progress int not null default 0,error text,path text,file_name text,file_size bigint not null default 0,
    created_at timestamptz not null default now(),started_at timestamptz,expires_at timestamptz,worker_id text,lease_until timestamptz
);
create index ix_export_queue on export_jobs(state,created_at);
create table audit_logs(id bigserial primary key,user_id bigint references users(id),action varchar(128) not null,resource text not null,summary varchar(4096),client_ip inet,created_at timestamptz not null default now());
create index ix_audit_time on audit_logs(created_at desc,id desc);
create table releases (
    id bigserial primary key,version varchar(32) not null unique,file_name varchar(255) not null,storage_name varchar(128) not null unique,
    sha256 char(64) not null,file_size bigint not null check(file_size>0),release_notes varchar(4096) not null default '',minimum_version varchar(32),
    force_update boolean not null default false,status text not null default 'draft' check(status in('draft','published','revoked')),
    download_count bigint not null default 0,published_at timestamptz,created_at timestamptz not null default now()
);
create table settings(id int primary key check(id=1),value jsonb not null,updated_at timestamptz not null default now());
create table outbox(id bigserial primary key,kind varchar(64) not null,resource_id text not null,user_id bigint references users(id),channel_id bigint references channels(id),device_id bigint references devices(id),version bigint not null default 1,created_at timestamptz not null default now(),delivered_at timestamptz);
create index ix_outbox_pending on outbox(id) where delivered_at is null;
create table service_heartbeats(name text primary key,checked_at timestamptz not null,details jsonb not null default '{}');
