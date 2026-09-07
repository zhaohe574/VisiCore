-- 保存 SDK 报警解析出的图片数据，结构化字段仍存放在 normalized_payload。
alter table alarm_events add column if not exists image_payload bytea;
