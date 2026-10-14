-- 016_onu_if_index.sql
-- 为 onu_devices 表增加 if_index 字段，供华为 eSight modifyOnuAliasName 接口入参使用

alter table onu_devices add column if not exists if_index varchar(64);
create index if not exists ix_onu_devices_if_index on onu_devices(if_index);
