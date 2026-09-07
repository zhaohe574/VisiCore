-- 防止报警文件在服务重启后重复入库。
alter table alarm_events add column if not exists event_hash char(64);
drop index if exists ux_alarm_events_hash;
create unique index if not exists ux_alarm_events_hash on alarm_events(event_hash);
