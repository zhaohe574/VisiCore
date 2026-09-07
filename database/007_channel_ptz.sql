-- 保存通道是否支持云台控制，兼容已执行 001_initial.sql 的旧数据库。
alter table channels add column if not exists ptz_capable boolean not null default false;
