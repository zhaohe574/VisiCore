-- 同一版本分别保留安装包与便携包，同类型包继续保持唯一。
alter table releases drop constraint releases_version_key;
create unique index releases_version_package_key on releases(version, (lower(right(file_name, 4))));
