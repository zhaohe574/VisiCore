-- 006_plugin_permissions 之后的增量迁移：放开布局分屏档位约束。
--
-- 背景：桌面端 2.1.0 按 iVMS-4200 增加「1 大屏 + n 小屏」聚焦档位，
-- 使用 6（1+5）与 8（1+7）两个格数。原约束只允许 1、4、9、16，
-- 保存这类方案会被数据库拒绝（data.constraint）。
--
-- 说明：layouts.layout 存的是「格数」，聚焦档位与等分档位的区别由格数本身表达，
-- 无需新增列；空窗口仍使用可空数组元素表示，语义不变。

alter table layouts drop constraint if exists layouts_layout_check;

alter table layouts add constraint layouts_layout_check
    check (layout in (1, 4, 6, 8, 9, 16));

comment on column layouts.layout is
    '分屏格数：1／4／9／16 为等分档位，6（1+5）与 8（1+7）为一大屏加多小屏的聚焦档位。';
