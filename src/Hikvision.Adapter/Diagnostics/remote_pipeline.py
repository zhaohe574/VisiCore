"""验证候选实际发布版本及录播导出链路，密钥仅在服务器内存使用。"""
import argparse
import base64
import hashlib
import os
from pathlib import Path

import paramiko


REMOTE = r'''
import datetime, hashlib, json, pathlib, subprocess, time, urllib.error, urllib.request

config = json.loads(pathlib.Path('/home/liteware/.config/video-platform/v2-production.json').read_text())
api = 'http://127.0.0.1:5082'
adapter = 'http://127.0.0.1:5092'
token = None
playback = None
jobs = []

def call(method, path, body=None, internal=False):
    headers = {'Content-Type': 'application/json'}
    if internal:
        headers['X-Adapter-Key'] = config['adapterKey']
    elif token:
        headers['Authorization'] = 'Bearer ' + token
    request = urllib.request.Request((adapter if internal else api) + path, method=method,
        headers=headers, data=json.dumps(body).encode() if body is not None else (b'' if method == 'POST' else None))
    try:
        with urllib.request.urlopen(request, timeout=90) as response:
            content = response.read()
            return json.loads(content) if content else None
    except urllib.error.HTTPError as error:
        detail = error.read().decode('utf-8', 'replace')
        for secret in config.values():
            if isinstance(secret, str) and secret:
                detail = detail.replace(secret, '[已隐藏]')
        raise RuntimeError(f'{method} {path}：HTTP {error.code} {detail}') from None

def require(condition, message):
    if not condition:
        raise RuntimeError(message)

def timestamp(value):
    return datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))

def wait_playing(session_id):
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        state = call('GET', '/api/v2/playback-sessions/' + session_id)
        if state['state'] == 'playing':
            return state
        if state['state'] in ('failed', 'stopped', 'completed'):
            internal = call('GET', f'/internal/devices/{DEVICE_ID}/playback/{session_id}', internal=True)
            raise RuntimeError('回放提前结束：' + state['state'] + '，原因：' + str(internal.get('error')))
        time.sleep(.5)
    raise RuntimeError('回放起流超时')

def sample_media(state):
    url = 'http://127.0.0.1:18082/' + state['httpFlvUrl'].split('/media/', 1)[1]
    with urllib.request.urlopen(url, timeout=20) as response:
        sample = response.read(65536)
    require(sample[:3] == b'FLV' and len(sample) == 65536, '回放 FLV 数据不足或格式不正确')
    return len(sample)

def export_status(job_id):
    return next(item for item in call('GET', '/api/v2/exports?pageSize=200')['items'] if item['id'] == job_id)

def verify_mp4(path):
    probe = subprocess.run(['/usr/bin/ffprobe', '-v', 'error', '-read_intervals', '%+#150', '-count_frames', '-select_streams', 'v:0', '-show_entries', 'format=duration:stream=codec_name,width,height,nb_read_frames', '-of', 'json', str(path)], capture_output=True, text=True, timeout=30)
    require(probe.returncode == 0, '导出成品 FFprobe 检查失败')
    data = json.loads(probe.stdout)
    require(bool(data.get('streams')), '导出成品缺少视频轨道')
    video = data['streams'][0]
    require(video.get('width', 0) > 0 and video.get('height', 0) > 0 and int(video.get('nb_read_frames', '0')) > 0, '导出成品没有可解码画面')
    print('导出解码', path.name, json.dumps(data, ensure_ascii=False), flush=True)

def run_playback(body):
    global playback
    playback = call('POST', '/api/v2/playback-sessions', dict(body, profile='browser'))
    session_id = playback['id']
    endpoint = '/api/v2/playback-sessions/' + session_id
    state = wait_playing(session_id)
    print('回放起流', json.dumps({key: state.get(key) for key in ('state', 'codec', 'transcoded', 'currentTime', 'speed')}, ensure_ascii=False), '字节', sample_media(state), flush=True)
    before = timestamp(state['currentTime'])
    time.sleep(2)
    state = call('GET', endpoint)
    require(timestamp(state['currentTime']) > before, '回放媒体时间未推进')
    paused = call('POST', endpoint + '/control', {'action': 'pause'})
    time.sleep(1.5)
    still = call('GET', endpoint)
    require(still['state'] == 'paused' and timestamp(still['currentTime']) == timestamp(paused['currentTime']), '暂停后媒体时间仍推进')
    print('暂停位置保持', still['currentTime'], flush=True)
    call('POST', endpoint + '/control', {'action': 'resume'})
    state = wait_playing(session_id)
    print('继续收流', sample_media(state), flush=True)
    state = call('POST', endpoint + '/control', {'action': 'speed', 'speed': 2})
    state = wait_playing(session_id)
    require(state['speed'] == 2, '倍速未生效')
    print('两倍速收流', sample_media(state), flush=True)
    position = timestamp(body['start']) + datetime.timedelta(seconds=45)
    call('POST', endpoint + '/control', {'action': 'seek', 'position': position.isoformat()})
    state = wait_playing(session_id)
    require(position <= timestamp(state['currentTime']) < position + datetime.timedelta(seconds=25), '定位后媒体位置不在目标范围')
    print('定位收流', sample_media(state), '位置', state['currentTime'], flush=True)
    call('DELETE', endpoint)
    call('DELETE', endpoint)
    playback = None
    print('重复停止通过', flush=True)

def run_export(body, device_id):
    job = call('POST', '/api/v2/exports', body)
    jobs.append(job['id'])
    deadline = time.monotonic() + 180
    previous = None
    while time.monotonic() < deadline:
        state = export_status(job['id'])
        marker = state['state'], state['progress']
        if marker != previous:
            print('导出进度', marker, flush=True)
            previous = marker
        if state['state'] in ('completed', 'failed', 'cancelled'):
            break
        time.sleep(1)
    if state['state'] != 'completed':
        internal = call('GET', f'/internal/devices/{device_id}/exports/{job["id"]}', internal=True)
        raise RuntimeError('导出未完成：' + str(internal.get('error') or state.get('error') or state['state']))
    saved = call('GET', f'/internal/devices/{device_id}/exports/{job["id"]}', internal=True)
    output = pathlib.Path(saved['path']).resolve()
    require(output.is_relative_to(pathlib.Path('/var/lib/video-platform/v2/exports')), '导出文件越出平台目录')
    require(output.stat().st_size == state['fileSize'], '导出成品大小与平台记录不一致')
    if output.suffix == '.mp4':
        verify_mp4(output)
    else:
        import zipfile, tempfile, shutil
        with zipfile.ZipFile(output) as archive:
            require(archive.testzip() is None and '时间清单.json' in archive.namelist(), '导出 ZIP 或时间清单无效')
            print('导出分段', archive.namelist(), flush=True)
            manifest = json.loads(archive.read('时间清单.json'))
            require(len(manifest['segments']) > 0, '导出时间清单没有片段')
            with tempfile.TemporaryDirectory(prefix='verify-mp4-', dir=output.parent) as temporary:
                for segment in manifest['segments']:
                    name = segment['fileName']
                    require(pathlib.Path(name).name == name and name.endswith('.mp4'), '清单文件名无效')
                    path = pathlib.Path(temporary) / name
                    with archive.open(name) as source, path.open('wb') as target:
                        shutil.copyfileobj(source, target, 1024 * 1024)
                    verify_mp4(path)
    headers = {'Authorization': 'Bearer ' + token, 'Range': 'bytes=0-65535'}
    with urllib.request.urlopen(urllib.request.Request(api + '/api/v2/exports/' + job['id'] + '/download', headers=headers), timeout=20) as response:
        require(len(response.read(65536)) > 0, '导出授权下载没有数据')
    print('导出授权下载通过', flush=True)

def run_cancel(body, device_id):
    end = timestamp(body['end'])
    job = call('POST', '/api/v2/exports', dict(body, start=(end - datetime.timedelta(minutes=30)).isoformat()))
    jobs.append(job['id'])
    endpoint = f'/internal/devices/{device_id}/exports/{job["id"]}'
    deadline = time.monotonic() + 30
    while time.monotonic() < deadline:
        state = export_status(job['id'])
        if state['state'] == 'running':
            try:
                active = call('GET', endpoint, internal=True)
                if active['state'] == 'running':
                    break
            except RuntimeError:
                pass
        require(state['state'] not in ('completed', 'failed', 'cancelled'), '取消前导出已结束：' + str(state.get('error') or state['state']))
        time.sleep(.2)
    else:
        raise RuntimeError('取消验证未等到实际下载开始')
    began = time.monotonic()
    call('POST', '/api/v2/exports/' + job['id'] + '/cancel')
    deadline = time.monotonic() + 45
    while time.monotonic() < deadline:
        active = call('GET', endpoint, internal=True)
        if active['state'] in ('cancelled', 'failed', 'completed'):
            break
        time.sleep(.2)
    require(active['state'] == 'cancelled', '适配器取消尚未确认：' + active['state'])
    work = pathlib.Path('/var/lib/video-platform/v2/exports') / job['id'] / ('d' + str(device_id) + '-' + job['id'].replace('-', ''))
    require(not work.exists() or not any(work.iterdir()), '取消确认后仍残留临时导出文件')
    duplicate = call('DELETE', endpoint, internal=True)
    require(duplicate['state'] == 'cancelled', '重复取消终态不一致')
    print('真实导出取消确认', round(time.monotonic() - began, 3), '秒，临时文件已清理', flush=True)

try:
    current = pathlib.Path('/opt/video-platform-v2/current/adapter/Hikvision.Adapter')
    process_id = subprocess.check_output(['systemctl', 'show', 'video-platform-v2-adapter', '-p', 'MainPID', '--value'], text=True).strip()
    process_directory = pathlib.Path('/proc/' + process_id + '/cwd').resolve()
    loaded = pathlib.Path('/proc/' + process_id + '/exe')
    digest = hashlib.sha256(loaded.read_bytes()).hexdigest()
    print('候选目录', current.resolve().parent, '进程目录', process_directory, '适配器 SHA256', digest, flush=True)
    print('匹配本地发布产物', digest == EXPECTED_HASH, flush=True)
    end = datetime.datetime.now(datetime.timezone.utc) - datetime.timedelta(minutes=10)
    start = end - datetime.timedelta(minutes=2)
    if RANGE_START:
        start = timestamp(RANGE_START)
        end = timestamp(RANGE_END)
    if MODE == 'inspect':
        try:
            data = call('POST', '/internal/devices/1/recordings/search', {'channel': 8, 'start': start.isoformat(), 'end': end.isoformat()}, internal=True)
            print('内部检索条目', len(data), flush=True)
        except RuntimeError as error:
            print('内部检索错误', error, flush=True)
    else:
        require(digest == EXPECTED_HASH, '候选尚未运行本地最新适配器，请主代理上传后重测')
        token = call('POST', '/api/v2/auth/login', {'username': 'admin', 'password': config['adminPassword'], 'clientType': 'desktop', 'clientVersion': '2.0.0-adapter-check'})['accessToken']
        channel = next(item for item in call('GET', '/api/v2/channels?pageSize=200')['items'] if item['deviceChannel'] == 8 and item['status'] == 'online')
        DEVICE_ID = channel['deviceId']
        body = {'channelId': channel['id'], 'start': start.isoformat(), 'end': end.isoformat()}
        recordings = call('POST', '/api/v2/recordings/search', body)
        require(bool(recordings), '测试时间段没有录像')
        print('真实录像检索条目', len(recordings), flush=True)
        if MODE in ('media', 'playback'):
            run_playback(body)
        if MODE in ('media', 'export'):
            run_export(body, channel['deviceId'])
            run_cancel(body, channel['deviceId'])
finally:
    if token:
        if playback:
            try:
                call('DELETE', '/api/v2/playback-sessions/' + playback['id'])
            except Exception:
                print('诊断回放清理失败，需平台继续回收', flush=True)
        for job_id in jobs:
            try:
                if export_status(job_id)['state'] in ('queued', 'running'):
                    call('POST', '/api/v2/exports/' + job_id + '/cancel')
            except Exception:
                print('诊断导出清理失败，需平台继续回收', flush=True)
        call('POST', '/api/v2/auth/logout')
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['inspect', 'media', 'export', 'playback'], default='inspect', nargs='?')
    parser.add_argument('--start', help='固定重测起点，ISO 8601 格式')
    parser.add_argument('--end', help='固定重测终点，ISO 8601 格式')
    args = parser.parse_args()
    if bool(args.start) != bool(args.end):
        parser.error('--start 和 --end 必须同时提供')
    binary = Path(__file__).resolve().parents[3] / 'artifacts/v2/adapter/Hikvision.Adapter'
    expected = hashlib.sha256(binary.read_bytes()).hexdigest()
    script = 'EXPECTED_HASH=' + repr(expected) + '\nMODE=' + repr(args.mode) + '\nRANGE_START=' + repr(args.start) + '\nRANGE_END=' + repr(args.end) + '\n' + REMOTE
    encoded = base64.b64encode(script.encode()).decode()
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    try:
        client.connect('10.37.200.74', username='liteware', password=os.environ['VIDEO_PLATFORM_SSH_PASSWORD'], timeout=15, look_for_keys=False, allow_agent=False)
        command = "sudo -S -p '' python3 -u -c \"import base64,sys;sys.excepthook=lambda t,v,b: print('诊断失败：'+str(v),file=sys.stderr);exec(base64.b64decode('" + encoded + "'))\""
        stdin, stdout, stderr = client.exec_command(command, timeout=600)
        stdin.write(os.environ['VIDEO_PLATFORM_SSH_PASSWORD'] + '\n')
        stdin.flush()
        stdin.channel.shutdown_write()
        for line in stdout:
            print(line.rstrip(), flush=True)
        error = stderr.read().decode('utf-8', 'replace')
        if error.strip():
            print(error[-4000:], flush=True)
        if stdout.channel.recv_exit_status():
            raise SystemExit(1)
    finally:
        client.close()


if __name__ == '__main__':
    main()
