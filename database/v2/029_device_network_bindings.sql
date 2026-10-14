-- 029_device_network_bindings.sql
-- 支持录像机与设备物理网络承载（所属终端箱、ONU 与端口）

ALTER TABLE devices ADD COLUMN IF NOT EXISTS terminal_box_id bigint REFERENCES terminal_boxes(id) ON DELETE SET NULL;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS onu_device_id bigint REFERENCES onu_devices(id) ON DELETE SET NULL;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS port_number integer;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS is_switch_port boolean DEFAULT false;
ALTER TABLE devices ADD COLUMN IF NOT EXISTS port_label varchar(64);

CREATE INDEX IF NOT EXISTS idx_devices_terminal_box ON devices(terminal_box_id);
CREATE INDEX IF NOT EXISTS idx_devices_onu_device ON devices(onu_device_id);

-- 绑定车间已登记的工位录像机物理拓扑
UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 4,
  is_switch_port = false,
  port_label = 'LAN4'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.24.21' AND tb.code = 'HG4-E1' AND o.sn LIKE '%C087B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 3,
  is_switch_port = false,
  port_label = 'LAN3'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.24.22' AND tb.code = 'HG4-E1' AND o.sn LIKE '%C087B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 4,
  is_switch_port = false,
  port_label = 'LAN4'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.24.30' AND tb.code = 'HG4-E1' AND o.sn LIKE '%C11FB8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 1,
  is_switch_port = false,
  port_label = 'LAN1'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.24.31' AND tb.code = 'HG5-E1' AND o.sn LIKE '%BF06B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 3,
  is_switch_port = false,
  port_label = 'LAN3'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.24.49' AND tb.code = 'HG3-W2' AND o.sn LIKE '%C0C5B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 4,
  is_switch_port = false,
  port_label = 'LAN4'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.24.50' AND tb.code = 'HG3-W2' AND o.sn LIKE '%C0C5B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 2,
  is_switch_port = true,
  port_label = 'SW-P2'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.32.31' AND tb.code = 'LX2-E1' AND o.sn LIKE '%C206B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 1,
  is_switch_port = true,
  port_label = 'SW-P1'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.32.32' AND tb.code = 'LX2-E1' AND o.sn LIKE '%C206B8%';

UPDATE devices d SET
  terminal_box_id = tb.id,
  onu_device_id = o.id,
  port_number = 2,
  is_switch_port = false,
  port_label = 'LAN2'
FROM terminal_boxes tb, onu_devices o
WHERE d.host = '10.37.32.35' AND tb.code = 'LX2-E1' AND o.sn LIKE '%C206B8%';
