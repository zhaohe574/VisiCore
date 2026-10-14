#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
batch_add_cameras.py
批量将车间探测在线摄像头添加至京华中央录像机（10.37.200.53）并录入视枢系统通道台账与网络承载拓扑。
"""

import sys
import os
import json
import time
import ssl
import urllib.request
import urllib.error
import argparse
import paramiko
import socket

def _required(name):
    """凭据一律取自环境变量，不提供默认值，避免真实口令进入源码与 Git 历史。"""
    value = os.environ.get(name)
    if not value:
        raise SystemExit(f"缺少必需的环境变量：{name}（请勿把真实口令写入源码或脚本）")
    return value

SERVER_IP = os.environ.get("VIDEO_PLATFORM_SERVER_IP", "10.37.200.74")
LOCAL_IP = os.environ.get("VIDEO_PLATFORM_BIND_IP", "10.37.6.210")
SSH_USER = os.environ.get("VIDEO_PLATFORM_SSH_USER", "liteware")
SSH_PASSWORD = _required("VIDEO_PLATFORM_SSH_PASSWORD")
ADMIN_USER = os.environ.get("VIDEO_PLATFORM_ADMIN_USER", "admin")
ADMIN_PASSWORD = _required("VIDEO_PLATFORM_ADMIN_PASSWORD")
# 摄像机口令按品牌从环境变量读取，缺省留空并要求显式提供。
IPC_PASSWORDS = {
    "jovision": os.environ.get("VIDEO_PLATFORM_IPC_PASSWORD_JOVISION", ""),
    "hikvision": os.environ.get("VIDEO_PLATFORM_IPC_PASSWORD_HIKVISION", ""),
}
API_BASE = f"https://{SERVER_IP}/api/v2"

def get_db_state():
    """通过 SSH 从生产数据库拉取当前最新的通道 IP、ONU 和终端箱拓扑映射"""
    s = socket.socket()
    if LOCAL_IP:
        s.bind((LOCAL_IP, 0))
    s.connect((SERVER_IP, 22))
    c = paramiko.SSHClient()
    c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    c.connect(SERVER_IP, username=SSH_USER, password=SSH_PASSWORD, sock=s)

    # 1. 已有通道 IP
    cmd = 'sudo -S -p "" -u postgres psql -d video_platform_v2 --pset=pager=off -t -A -c "SELECT ip FROM channels WHERE ip IS NOT NULL AND length(ip) > 0;"'
    stdin, out, _ = c.exec_command(cmd)
    stdin.write(f'{SSH_PASSWORD}\n')
    stdin.flush()
    existing_ips = set(l.strip() for l in out.read().decode('utf-8', 'replace').strip().split('\n') if l.strip())

    # 2. ONU 拓扑映射
    cmd2 = 'sudo -S -p "" -u postgres psql -d video_platform_v2 --pset=pager=off -t -A -c "SELECT id, sn, alias, terminal_box_id FROM onu_devices;"'
    stdin2, out2, _ = c.exec_command(cmd2)
    stdin2.write(f'{SSH_PASSWORD}\n')
    stdin2.flush()
    onus_raw = [l.strip().split('|') for l in out2.read().decode('utf-8', 'replace').strip().split('\n') if l.strip()]

    # 3. 终端箱映射
    cmd3 = 'sudo -S -p "" -u postgres psql -d video_platform_v2 --pset=pager=off -t -A -c "SELECT id, code, name FROM terminal_boxes;"'
    stdin3, out3, _ = c.exec_command(cmd3)
    stdin3.write(f'{SSH_PASSWORD}\n')
    stdin3.flush()
    boxes = {l.strip().split('|')[1]: {'id': int(l.strip().split('|')[0]), 'name': l.strip().split('|')[2]} 
             for l in out3.read().decode('utf-8', 'replace').strip().split('\n') if '|' in l}

    c.close()

    onus = {}
    for r in onus_raw:
        if len(r) >= 4:
            sn = r[1].strip().upper()
            onus[sn] = {
                'id': int(r[0]),
                'sn': sn,
                'alias': r[2].strip() if r[2] else '',
                'box_id': int(r[3]) if r[3] else None
            }

    return existing_ips, onus, boxes

def match_onu(sn_raw, onus_dict):
    if not sn_raw:
        return None
    sn_clean = sn_raw.strip().upper()
    for sn_key, onu in onus_dict.items():
        if sn_key == sn_clean or sn_key.endswith(sn_clean):
            return onu
    return None

def login():
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE

    url_login = f"{API_BASE}/auth/login"
    data = json.dumps({
        'username': ADMIN_USER,
        'password': ADMIN_PASSWORD,
        'clientType': 'desktop',
        'clientVersion': '2.0.0'
    }).encode()
    req = urllib.request.Request(url_login, data=data, headers={'Content-Type': 'application/json', 'User-Agent': 'Mozilla/5.0'})
    with urllib.request.urlopen(req, context=ctx, timeout=10) as resp:
        return json.loads(resp.read().decode())['accessToken']

def add_camera_api(token, payload):
    ctx = ssl.create_default_context()
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE

    url_add = f"{API_BASE}/organization/channels/add-camera"
    data = json.dumps(payload).encode()
    req = urllib.request.Request(url_add, data=data, headers={
        'Authorization': f'Bearer {token}',
        'Content-Type': 'application/json',
        'User-Agent': 'Mozilla/5.0'
    })
    with urllib.request.urlopen(req, context=ctx, timeout=25) as resp:
        return json.loads(resp.read().decode())

def main():
    parser = argparse.ArgumentParser(description="批量将车间摄像头添加至京华中央录像机与通道台账")
    parser.add_argument("--limit", type=int, default=0, help="限制添加数量（0 表示不限制，处理全部）")
    parser.add_argument("--probe-json", default="点位IP探测全量结果.json", help="点位探测结果 JSON 路径")
    parser.add_argument("--dry-run", action="store_true", help="演练模式，仅输出待添加计划不实际调用 API")
    args = parser.parse_args()

    print("=" * 80)
    print("视枢系统第三步：批量向中央录像机（10.37.200.53）与通道台账添加车间摄像头")
    print("=" * 80)

    # 1. 登录获取 Token
    print("[1/4] 正在登录系统获取管理鉴权 Token...")
    try:
        token = login()
        print("  ✓ 鉴权成功，Token 已生成")
    except Exception as e:
        print(f"  ✗ 登录失败: {e}")
        sys.exit(1)

    # 2. 同步生产数据库最新状态
    print("[2/4] 正在拉取生产数据库现有通道与网络拓扑基线...")
    existing_ips, onus_dict, boxes_dict = get_db_state()
    print(f"  ✓ 现有已登记通道 IP: {len(existing_ips)} 个")
    print(f"  ✓ 现有 ONU 设备: {len(onus_dict)} 台")
    print(f"  ✓ 现有终端箱: {len(boxes_dict)} 个")

    # 3. 读取并筛选待入库在线摄像头
    probe_path = args.probe_json
    if not os.path.exists(probe_path):
        candidate = os.path.join(os.path.dirname(__file__), "..", "docs", "field-data", os.path.basename(probe_path))
        if os.path.exists(candidate):
            probe_path = os.path.normpath(candidate)
    print(f"[3/4] 正在解析点位探测文件: {probe_path}...")
    if not os.path.exists(probe_path):
        print(f"  ✗ 文件未找到: {args.probe_json} (且未在 docs/field-data 中找到)")
        sys.exit(1)

    with open(probe_path, "r", encoding="utf-8") as f:
        probe_data = json.load(f)

    online_ipcs = [r for r in probe_data if r.get('is_online') and 'IPC' in r.get('detected_type', '')]
    pending = [r for r in online_ipcs if r.get('ip', '').strip() not in existing_ips]

    print(f"  ✓ 探测在线 IPC 总数: {len(online_ipcs)} 台")
    print(f"  ✓ 待入库 IPC 数量: {len(pending)} 台")

    if args.limit > 0:
        pending = pending[:args.limit]
        print(f"  ℹ 已按 --limit 截取前 {args.limit} 台进行执行")

    if not pending:
        print("  ✓ 没有需要添加的摄像头，全部在线摄像头均已入库！")
        return

    # 4. 逐台构建请求并调用 API 添加
    print(f"[4/4] 开始批量添加 {len(pending)} 台摄像头至中央录像机与通道台账...")
    print("-" * 80)

    success_list = []
    fail_list = []
    start_time = time.time()

    for idx, r in enumerate(pending, 1):
        ip = r.get('ip', '').strip()
        brand = r.get('final_brand', '')
        dev_name = (r.get('dev_name') or '').strip()
        alias = dev_name if dev_name else f"IPC-{ip}"
        
        # 协议与凭据（口令来自环境变量，源码中不保留任何真实口令）
        if "中维" in brand:
            proto = "ONVIF"
            port = 80
            user = "admin"
            pwd = IPC_PASSWORDS["jovision"]
        elif "海康" in brand:
            proto = "HIKVISION"
            port = 8000
            user = "admin"
            pwd = IPC_PASSWORDS["hikvision"]
        else:
            proto = "ONVIF"
            port = 80
            user = "admin"
            pwd = IPC_PASSWORDS["jovision"]

        if not pwd:
            print(f"[{idx}/{len(pending)}] ✗ 跳过 {ip} ({alias}): 未配置该品牌的摄像机口令环境变量")
            fail_list.append({'ip': ip, 'alias': alias, 'error': "IPC password not configured for brand"})
            continue

        # 匹配 ONU
        onu = match_onu(r.get('sn', ''), onus_dict)
        if not onu:
            print(f"[{idx}/{len(pending)}] ✗ 跳过 {ip} ({alias}): 未找到匹配的 ONU (SN={r.get('sn')})")
            fail_list.append({'ip': ip, 'alias': alias, 'error': f"ONU not found for SN {r.get('sn')}"})
            continue

        box_id = onu['box_id']
        if not box_id:
            box_code = r.get('box', '').strip()
            if box_code in boxes_dict:
                box_id = boxes_dict[box_code]['id']
            else:
                print(f"[{idx}/{len(pending)}] ✗ 跳过 {ip} ({alias}): 未找到匹配的终端箱")
                fail_list.append({'ip': ip, 'alias': alias, 'error': "Terminal box not found"})
                continue

        # 端口与交换机判定
        is_switch = (r.get('workshop') == '交换机接入') or ('交换机' in r.get('remark', ''))
        port_raw = str(r.get('port', '')).strip()
        port_num = int(port_raw) if port_raw.isdigit() and int(port_raw) > 0 else 1
        port_label = f"SW-P{port_num}" if is_switch else f"LAN{port_num}"

        payload = {
            "nvrDeviceId": 1,
            "ipcHost": ip,
            "ipcPort": port,
            "ipcUsername": user,
            "ipcPassword": pwd,
            "protocolType": proto,
            "alias": alias,
            "name": alias,
            "terminalBoxId": box_id,
            "onuDeviceId": onu['id'],
            "portNumber": port_num,
            "isSwitchPort": is_switch,
            "portLabel": port_label
        }

        if args.dry_run:
            print(f"[{idx}/{len(pending)}] [DRY-RUN] {ip} ({alias}) -> NVR1 | {proto}:{port} | ONU:{onu['alias']} | Box:{box_id} | {port_label}")
            success_list.append({'ip': ip, 'alias': alias, 'payload': payload})
            continue

        # 实际调用 API
        t0 = time.time()
        try:
            res = add_camera_api(token, payload)
            elapsed = time.time() - t0
            chan_id = res.get('id')
            dev_chan = res.get('deviceChannel')
            print(f"[{idx}/{len(pending)}] 🟢 成功: {ip} | {alias} -> 通道 {dev_chan} (ID={chan_id}) | {proto} | {onu['alias']} | {port_label} ({elapsed:.1f}s)")
            success_list.append({
                'ip': ip,
                'alias': alias,
                'channelId': chan_id,
                'deviceChannel': dev_chan,
                'brand': brand,
                'proto': proto,
                'onuAlias': onu['alias'],
                'portLabel': port_label,
                'elapsed': elapsed
            })
        except urllib.error.HTTPError as e:
            err_body = e.read().decode('utf-8', 'replace')
            elapsed = time.time() - t0
            print(f"[{idx}/{len(pending)}] 🔴 失败: {ip} | {alias} -> HTTP {e.code}: {err_body} ({elapsed:.1f}s)")
            fail_list.append({'ip': ip, 'alias': alias, 'error': f"HTTP {e.code}: {err_body}"})
        except Exception as e:
            elapsed = time.time() - t0
            print(f"[{idx}/{len(pending)}] 🔴 失败: {ip} | {alias} -> 异常: {e} ({elapsed:.1f}s)")
            fail_list.append({'ip': ip, 'alias': alias, 'error': str(e)})

        # 每次添加微小延迟以防录像机 ISAPI 拥塞
        time.sleep(0.15)

    total_elapsed = time.time() - start_time
    print("=" * 80)
    print(f"批量添加完成！总耗时: {total_elapsed:.1f} 秒 ({total_elapsed/60:.1f} 分钟)")
    print(f"成功: {len(success_list)} 台 | 失败: {len(fail_list)} 台")
    print("=" * 80)

    # 写入结果明细文件
    summary_path = "批量添加摄像头执行结果.json"
    with open(summary_path, "w", encoding="utf-8") as f:
        json.dump({
            'total_pending': len(pending),
            'success_count': len(success_list),
            'fail_count': len(fail_list),
            'total_elapsed_sec': round(total_elapsed, 1),
            'success_items': success_list,
            'fail_items': fail_list
        }, f, ensure_ascii=False, indent=2)
    print(f"详细执行报告已保存至: {summary_path}")

if __name__ == "__main__":
    main()
