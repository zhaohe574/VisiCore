-- 030_splitter_broken_and_root_cause.sql
-- 光链路智能故障定界多级重构：
-- 1. 为 optical_health_snapshots 增加 splitter_broken_onus（分光器/支干中断）字段
-- 2. 根因与定界机制升级：
--    - 收到 Dying Gasp 掉电遗言优先定界为供电中断（同箱多台为“终端箱掉电”，单台为“设备掉电”）
--    - 仅当同 PON 口全部设备失联（在线数 = 0 且总数 >= 2）时才判定为主干光缆中断
--    - 同 PON 口无掉电遗言的多台在 3 分钟内并发失联判定为分光器/支干中断
--    - 单台离线设备按 Cause 2 (LOSi) 判定为分支断纤，其余判定为分支链路中断待排查

alter table optical_health_snapshots add column if not exists splitter_broken_onus integer not null default 0;

-- 纠偏现有数据：重置历史错误的健康状态与根因定界
-- 1. 掉电（Dying Gasp）优先重估
update onu_devices
set 
    health_status = 'power_off',
    root_cause = case 
        when terminal_box_id is not null and (
            select count(*) from onu_devices o2 
            where o2.terminal_box_id = onu_devices.terminal_box_id 
              and not (o2.status in ('1', 'online') or o2.run_status = '1')
              and o2.last_dying_gasp_time is not null
              and o2.last_down_time is not null
              and abs(extract(epoch from (o2.last_down_time - o2.last_dying_gasp_time))) <= 15
        ) >= 2 then '终端箱掉电 / 总供电中断 ⚡ (同箱多台收到 Dying Gasp 掉电遗言)'
        else '设备掉电 / 供电中断 ⚡ (收到网管 Dying Gasp 掉电遗言)'
    end
where not (status in ('1', 'online') or run_status = '1')
  and last_dying_gasp_time is not null
  and last_down_time is not null
  and abs(extract(epoch from (last_down_time - last_dying_gasp_time))) <= 15;

-- 2. 分光器/支干中断（同 PON 口无掉电遗言、3 分钟内并发失联 >= 2 台）
update onu_devices
set 
    health_status = 'splitter_broken',
    root_cause = '分光器/支干光缆中断 💥 (同 PON 口 3 分钟内并发失联，疑似分支主干或分光器损坏)'
where not (status in ('1', 'online') or run_status = '1')
  and not (last_dying_gasp_time is not null and last_down_time is not null and abs(extract(epoch from (last_down_time - last_dying_gasp_time))) <= 15)
  and last_down_time is not null
  and (
      select count(*) from onu_devices o3
      where coalesce(o3.if_desc, 'GPON 0/1/' || o3.port_id::text) = coalesce(onu_devices.if_desc, 'GPON 0/1/' || onu_devices.port_id::text)
        and not (o3.status in ('1', 'online') or o3.run_status = '1')
        and not (o3.last_dying_gasp_time is not null and o3.last_down_time is not null and abs(extract(epoch from (o3.last_down_time - o3.last_dying_gasp_time))) <= 15)
        and o3.last_down_time is not null
        and abs(extract(epoch from (o3.last_down_time - onu_devices.last_down_time))) <= 180
  ) >= 2;

-- 3. 单台分支断纤/链路中断
update onu_devices
set 
    health_status = 'fiber_cut',
    root_cause = case 
        when last_down_cause = '2' then '分支光纤断纤 / 尾纤松脱 🔴 (网管上报 LOSi 物理失光)'
        else '分支链路中断 / 离线待排查 🔴 (Cause: ' || coalesce(last_down_cause, '13') || ')'
    end
where not (status in ('1', 'online') or run_status = '1')
  and health_status not in ('power_off', 'splitter_broken');

-- 4. 彻底消除错误的主干中断（若同口仍有在线设备，不得为主干中断）
update onu_devices
set 
    health_status = 'fiber_cut',
    root_cause = '分支链路中断 / 离线待排查 🔴 (Cause: ' || coalesce(last_down_cause, '13') || ')'
where health_status = 'trunk_broken'
  and (
      select count(*) from onu_devices o4
      where coalesce(o4.if_desc, 'GPON 0/1/' || o4.port_id::text) = coalesce(onu_devices.if_desc, 'GPON 0/1/' || onu_devices.port_id::text)
        and (o4.status in ('1', 'online') or o4.run_status = '1')
  ) > 0;
