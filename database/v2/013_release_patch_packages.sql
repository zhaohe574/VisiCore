-- 支持同一版本下分别保留 MSI 安装包、ZIP 便携包与 Patch 增量更新包，同类型包保持唯一。
drop index if exists releases_version_package_key;
create unique index releases_version_package_key on releases(
    version, 
    (case 
        when lower(file_name) like '%.msi' then 'msi' 
        when lower(file_name) like '%patch%.zip' or lower(file_name) like '%patch%' then 'patch' 
        else 'zip' 
    end)
);
