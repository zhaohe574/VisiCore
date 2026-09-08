"""在数据库迁移前保存第二版数据库快照，仅输出备份路径与校验值。"""

import base64
import os
import paramiko


REMOTE = r'''
from datetime import datetime, timezone
import hashlib, os, pathlib, subprocess
os.umask(0o077)
directory = pathlib.Path('/var/backups/video-platform') / ('v2-' + datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ'))
directory.mkdir(parents=True, mode=0o700)
database = directory / 'video_platform_v2.dump'
with database.open('xb') as output:
    result = subprocess.run(['runuser','-u','postgres','--','pg_dump','-Fc','video_platform_v2'], stdout=output, stderr=subprocess.PIPE)
if result.returncode: raise RuntimeError('数据库备份失败')
with database.open('rb') as source:
    result = subprocess.run(['pg_restore','--list'], stdin=source, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE)
if result.returncode: raise RuntimeError('数据库备份目录校验失败')
current = pathlib.Path('/opt/video-platform-v2/current').resolve()
(directory / 'release.txt').write_text(str(current) + '\n')
sources = ['/home/liteware/.config/video-platform', '/etc/nginx', '/etc/systemd/system/video-platform-v2-api.service',
           '/etc/systemd/system/video-platform-v2-worker.service', '/etc/systemd/system/video-platform-v2-adapter.service',
           '/etc/systemd/system/video-platform-v2-zlm.service', '/var/lib/video-platform/v2/zlm/config.ini']
result = subprocess.run(['tar','-czf',str(directory / 'configuration.tar.gz'),*sources], capture_output=True)
if result.returncode: raise RuntimeError('配置和证书备份失败')
for path in (database, directory / 'configuration.tar.gz'):
    with path.open('rb') as source: digest = hashlib.file_digest(source, 'sha256').hexdigest()
    print(path.name, path.stat().st_size, digest, flush=True)
print('备份目录：' + str(directory), flush=True)
'''


def main():
    client = paramiko.SSHClient()
    client.load_system_host_keys()
    try:
        password = os.environ['VIDEO_PLATFORM_SSH_PASSWORD']
        client.connect('10.37.200.74', username='liteware', password=password, timeout=15,
                       look_for_keys=False, allow_agent=False)
        encoded = base64.b64encode(REMOTE.encode()).decode()
        command = "sudo -S -p '' python3 -u -c \"import base64;exec(base64.b64decode('" + encoded + "'))\""
        input_stream, output, error = client.exec_command(command, timeout=180)
        input_stream.write(password + '\n')
        input_stream.flush()
        input_stream.channel.shutdown_write()
        for line in output: print(line.rstrip(), flush=True)
        if output.channel.recv_exit_status():
            print('备份失败，请检查服务器磁盘和备份目录权限。')
            return 1
        return 0
    finally:
        client.close()


if __name__ == '__main__':
    raise SystemExit(main())
