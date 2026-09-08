"""验证候选 HTTPS MPEG-TS 的访问控制和 H.264/H.265 实际解码。"""

import argparse
import base64
import os
from urllib.parse import urlsplit
import paramiko


REMOTE = r'''
import json, pathlib, ssl, subprocess, urllib.request, urllib.error
config = json.loads(pathlib.Path('/home/liteware/.config/video-platform/v2-production.json').read_text())
api = 'http://127.0.0.1:5082'
credential = None
active = []
def call(method, route, body=None):
    headers = {'Content-Type': 'application/json'}
    if credential: headers['Authorization'] = 'Bearer ' + credential
    request = urllib.request.Request(api + route, headers=headers, method=method,
        data=json.dumps(body).encode() if body is not None else (b'' if method == 'POST' else None))
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read()
        return json.loads(data) if data else None
try:
    credential = call('POST', '/api/v2/auth/login', {'username':'admin','password':config['adminPassword'], 'clientType':'desktop', 'clientVersion':'2.0.0-ts-check'})['accessToken']
    channels = call('GET', '/api/v2/channels?pageSize=200')['items']
    codecs = set()
    context = ssl.create_default_context(cafile='/etc/nginx/ssl/video-platform.crt')
    for local_id in (1, 2):
        channel = next(item for item in channels if item['deviceChannel'] == local_id)
        grant = call('POST', '/api/v2/live-sessions', {'channelId':channel['id'], 'streamType':1, 'profile':'native'})
        active.append(grant['id'])
        assert not grant['transcoded'], '原生预览不能占用视频转码'
        url = grant['httpTsUrl']
        assert url.startswith(expected_base + '/media/')
        try:
            urllib.request.urlopen(url.split('?')[0], context=context, timeout=10)
        except urllib.error.HTTPError as error:
            assert error.code in (401,403), '无令牌请求未被权限钩子拒绝'
        else: raise AssertionError('无令牌可访问媒体')
        with urllib.request.urlopen(url, context=context, timeout=20) as response:
            sample = response.read(188 * 100)
        assert len(sample) == 188 * 100 and all(sample[i] == 0x47 for i in range(0,len(sample),188)), '未取得有效 MPEG-TS 数据'
        internal = 'http://127.0.0.1:18082/' + url.split('/media/',1)[1]
        process = subprocess.run(['/usr/bin/ffprobe', '-v','error','-read_intervals','%+2','-count_frames','-select_streams','v:0','-show_entries','stream=codec_name,width,height,nb_read_frames','-of','json',internal], capture_output=True,text=True,timeout=40)
        assert process.returncode == 0, '媒体解码检查失败'
        video = json.loads(process.stdout)['streams'][0]
        assert int(video.get('nb_read_frames','0')) > 0 and video['width'] > 0 and video['height'] > 0, '没有可解码帧'
        codecs.add(video['codec_name'])
        print('HTTPS证书、未授权拒绝、原生解码通过：通道', local_id, json.dumps(video,ensure_ascii=False), flush=True)
        call('DELETE','/api/v2/live-sessions/'+grant['id'])
        active.remove(grant['id'])
    assert 'hevc' in codecs and 'h264' in codecs, '尚未覆盖两种真实编码'
finally:
    for session in active:
        try: call('DELETE','/api/v2/live-sessions/'+session)
        except Exception: print('验证媒体清理失败，需平台回收。',flush=True)
    if credential: call('POST','/api/v2/auth/logout')
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base-url', default='https://10.37.200.74:8443')
    args = parser.parse_args()
    parsed = urlsplit(args.base_url)
    if parsed.scheme != 'https' or parsed.hostname != '10.37.200.74' or parsed.path not in ('', '/') or parsed.query or parsed.fragment or parsed.username or parsed.password:
        parser.error('验证地址必须是目标服务器的 HTTPS 根地址。')
    ssh = paramiko.SSHClient()
    ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    try:
        ssh.connect('10.37.200.74', username='liteware', password=os.environ['VIDEO_PLATFORM_SSH_PASSWORD'],
                    timeout=15, look_for_keys=False, allow_agent=False)
        encoded = base64.b64encode(('expected_base=' + repr(args.base_url.rstrip('/')) + '\n' + REMOTE).encode()).decode()
        command = "sudo -S -p '' python3 -u -c \"import base64,sys;sys.excepthook=lambda t,v,b: print('媒体验证失败：'+t.__name__,file=sys.stderr);exec(base64.b64decode('" + encoded + "'))\""
        input_stream, output, error = ssh.exec_command(command, timeout=180)
        input_stream.write(os.environ['VIDEO_PLATFORM_SSH_PASSWORD'] + '\n')
        input_stream.flush()
        input_stream.channel.shutdown_write()
        for line in output: print(line.rstrip(), flush=True)
        message = error.read().decode('utf-8', 'replace')
        if message: print(message, flush=True)
        return output.channel.recv_exit_status()
    finally:
        ssh.close()


if __name__ == '__main__':
    raise SystemExit(main())
