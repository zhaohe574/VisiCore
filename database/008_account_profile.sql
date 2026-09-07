-- 账号资料字段。
alter table users add column if not exists display_name varchar(128);
alter table users add column if not exists phone varchar(32);

create index if not exists ix_users_phone on users(phone) where phone is not null;
