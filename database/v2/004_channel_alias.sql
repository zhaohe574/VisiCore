alter table channels add column if not exists alias varchar(256);
create index if not exists ix_channels_alias on channels(alias);
