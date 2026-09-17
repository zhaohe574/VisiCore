alter table channels add column if not exists ip varchar(253);
alter table channels add column if not exists username varchar(128);
alter table channels add column if not exists password varchar(256);
alter table channels add column if not exists remark text;
create index if not exists ix_channels_ip on channels(ip);
