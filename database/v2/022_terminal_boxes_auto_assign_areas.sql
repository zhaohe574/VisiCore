-- 022_terminal_boxes_auto_assign_areas.sql
-- 自动补全全网 97 个网络终端箱归属业务车间与区域映射台账

-- 1. 创建缺失的业务区域
-- 焊管车间下属
INSERT INTO areas (parent_id, name, code, status)
SELECT id, '水压车间', 'SYJ01', 'active' FROM workshops WHERE code = 'HG'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '出口管车间', 'CKG01', 'active' FROM workshops WHERE code = 'HG'
ON CONFLICT (parent_id, code) DO NOTHING;

-- 热镀车间下属
INSERT INTO areas (parent_id, name, code, status)
SELECT id, '热镀车间', 'RD01', 'active' FROM workshops WHERE code = 'RD'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '锌下料库', 'XXL01', 'active' FROM workshops WHERE code = 'RD'
ON CONFLICT (parent_id, code) DO NOTHING;

-- 外围下属
INSERT INTO areas (parent_id, name, code, status)
SELECT id, '宿舍区', 'SSL01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '磅房', 'BF01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '餐饮后勤', 'ST01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '涂塑车间', 'TS01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '喷漆车间', 'PQG01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '机加工', 'JJG01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '备件库', 'BJK01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '危废库', 'WFK01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '废料场', 'FLC01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

INSERT INTO areas (parent_id, name, code, status)
SELECT id, '化验室', 'HYS01', 'active' FROM workshops WHERE code = 'YW'
ON CONFLICT (parent_id, code) DO NOTHING;

-- 2. 映射全网 97 个终端箱至对应区域
-- 螺旋车间 (11)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'LX' AND a.code = 'LX01'
  AND tb.code IN ('LX1-W1', 'LX1-W2', 'LX1-W3', 'LX1-E1', 'LX1-E2', 'LX1-E3', 'LX2-E1', 'LX2-E2', 'LX3-E1', 'LX3-E2', 'LX3-E3');

-- 焊管一车间 (17)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'HG' AND a.code = 'HG01'
  AND tb.code IN ('HG1-W1', 'HG1-E1', 'HG1-E2', 'HG2-E1', 'HG2-E2', 'HG3-W1', 'HG3-W2', 'HG3-E1', 'HG3-E2',
                  'HG7-W1', 'HG7-W2', 'HG7-E1', 'HG7-E2', 'HG8-E1', 'HG8-E2', 'HG9-E1', 'HG9-E2');

-- 焊管二车间 (8)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'HG' AND a.code = 'HG02'
  AND tb.code IN ('HG4-W1', 'HG4-W2', 'HG4-E1', 'HG4-E2', 'HG5-E1', 'HG5-E2', 'HG6-E1', 'HG6-E2');

-- 水压车间 (4)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'HG' AND a.code = 'SYJ01'
  AND tb.code IN ('SYJ-W1', 'SYJ-W2', 'SYJ-E1', 'SYJ-E2');

-- 出口管车间 (1)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'HG' AND a.code = 'CKG01'
  AND tb.code IN ('CKG-01');

-- 热镀车间 (34)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'RD' AND a.code = 'RD01'
  AND tb.code IN (
    'RD1-W1', 'RD1-W2', 'RD1-E1', 'RD1-E2', 'RD1-E3',
    'RD2-W1', 'RD2-W2', 'RD2-E1', 'RD2-E2', 'RD2-E3',
    'RD3-01', 'RD3-02',
    'RD4-W1', 'RD4-W2', 'RD4-W3', 'RD4-E1', 'RD4-E2',
    'RD5-W1', 'RD5-W2', 'RD5-E1', 'RD5-E2', 'RD5-E3',
    'RD6-01', 'RD6-02',
    'RD7-W1', 'RD7-W2', 'RD7-W3', 'RD7-E1', 'RD7-E2',
    'RD8-W1', 'RD8-W2', 'RD8-W3', 'RD8-E1', 'RD8-E2'
  );

-- 锌下料库 (1)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'RD' AND a.code = 'XXL01'
  AND tb.code IN ('XXL-01');

-- 办公楼 (1)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'BGL'
  AND tb.code IN ('BGL-SW01');

-- 厂区保卫 (3)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'BW'
  AND tb.code IN ('JWS-01', 'JWS-02', 'JWS-03');

-- 宿舍区 (5)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'SSL01'
  AND tb.code IN ('SSL-01', 'SSL-02', 'SSL-03', 'SSL-04', 'DSL-01');

-- 磅房 (2)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'BF01'
  AND tb.code IN ('GYB-01', 'XSB-01');

-- 餐饮后勤 (1)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'ST01'
  AND tb.code IN ('DST-01');

-- 涂塑车间 (2)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'TS01'
  AND tb.code IN ('TS1-01', 'TS1-02');

-- 喷漆车间 (2)
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'PQG01'
  AND tb.code IN ('PQG-01', 'PQG-02');

-- 生产辅助各点
UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'JJG01' AND tb.code = 'JJG-01';

UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'BJK01' AND tb.code = 'BJK-01';

UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'WFK01' AND tb.code = 'WFK-01';

UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'FLC01' AND tb.code = 'FLC-01';

UPDATE terminal_boxes tb SET area_id = a.id
FROM areas a JOIN workshops w ON w.id = a.parent_id
WHERE w.code = 'YW' AND a.code = 'HYS01' AND tb.code = 'HYS-01';
