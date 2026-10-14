-- 019_clean_channel_port_labels.sql
-- 精简并规范化 channel_network_bindings 中的网口标签：
-- 1. 将 "ONU LANn 物理网口" 或 "ONU LANn" 规整为简洁的 "LANn"（如 LAN1, LAN2, LAN3, LAN4）
-- 2. 移除冗余的 " 物理网口" 后缀，避免挤占前端 UI 摄像头名称与 IP 展示空间

UPDATE channel_network_bindings
SET port_label = regexp_replace(port_label, '^ONU\s*LAN(\d+)\s*(物理网口)?$', 'LAN\1')
WHERE port_label ~* '^ONU\s*LAN\d+';

UPDATE channel_network_bindings
SET port_label = regexp_replace(port_label, '\s*物理网口$', '')
WHERE port_label LIKE '%物理网口';
