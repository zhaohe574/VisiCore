-- 018_fix_onu_timestamps_timezone.sql
-- 修复华为 eSight 网管时间（东八区/北京时间）导入时未附加时区偏移导致的 8 小时超前偏差

-- 1. 修正 onu_devices 历史记录中的 eSight 导入时间（减去 8 小时恢复真实 UTC）
update onu_devices
set last_down_time = last_down_time - interval '8 hours'
where last_down_time is not null;

update onu_devices
set last_up_time = last_up_time - interval '8 hours'
where last_up_time is not null;

-- 2. 修正 device_offline_history 中 ONU 设备的掉线发生时间与恢复时间
update device_offline_history
set down_time = down_time - interval '8 hours'
where device_type = 'onu';

update device_offline_history
set up_time = up_time - interval '8 hours'
where device_type = 'onu' and up_time is not null;

-- 重新计算已闭合事件的持续秒数
update device_offline_history
set duration_seconds = greatest(0, extract(epoch from (up_time - down_time))::int)
where device_type = 'onu' and up_time is not null;

-- 3. 清理并发或重复同步产生的多余进行中记录（针对同一 ONU 仅保留最早的一条处于掉线中的记录）
delete from device_offline_history a
using device_offline_history b
where a.onu_device_id is not null
  and a.onu_device_id = b.onu_device_id
  and a.up_time is null
  and b.up_time is null
  and a.id > b.id;
