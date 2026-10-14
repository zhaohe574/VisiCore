-- 021_terminal_boxes_area_scope.sql
-- 优化网络终端箱空间归属：由单元(unit)降维至区域(area)，强化物理安装位置区分

-- 1. 为 terminal_boxes 表增加 area_id 字段
alter table terminal_boxes add column if not exists area_id bigint references areas(id) on delete set null;
create index if not exists ix_terminal_boxes_area on terminal_boxes(area_id);

-- 2. 数据平滑迁移：将既有设置了 unit_id 的记录自动映射为其所属的 area_id
update terminal_boxes tb
set area_id = u.parent_id
from units u
where tb.unit_id = u.id and tb.area_id is null;
