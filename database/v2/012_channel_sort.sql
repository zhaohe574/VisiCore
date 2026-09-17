alter table channels add column if not exists sort_order int not null default 0;
create index if not exists ix_channels_unit_sort on channels(unit_id, sort_order, id);
