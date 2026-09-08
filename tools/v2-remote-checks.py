"""通过 SSH 执行候选服务验证，只回传脱敏验证结果。"""
import argparse
import base64
import os
from pathlib import Path
import paramiko

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("check", choices=["health", "device", "media", "access", "logs", "switch", "rollback", "publish"])
    parser.add_argument("--transport", choices=["flv", "ts"], default="flv", help="access 验证使用的媒体格式，默认 flv")
    parser.add_argument("--snapshot", help="rollback 的最近一次操作快照编号")
    args = parser.parse_args()
    if args.snapshot and args.check != "rollback":
        parser.error("--snapshot 仅适用于 rollback")
    if args.transport != "flv" and args.check != "access":
        parser.error("--transport 仅适用于 access")
    client = paramiko.SSHClient()
    client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
    sock = None
    bind_ip = os.environ.get("VIDEO_PLATFORM_BIND_IP", "10.37.6.210" if os.name == "nt" else None)
    if bind_ip:
        import socket
        try:
            s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            s.bind((bind_ip, 0))
            s.connect(("10.37.200.74", 22))
            sock = s
        except Exception:
            sock = None
    client.connect("10.37.200.74", username="liteware", password=os.environ["VIDEO_PLATFORM_SSH_PASSWORD"], sock=sock, timeout=15, look_for_keys=False, allow_agent=False)
    prefix = '''
import json, pathlib, urllib.request, urllib.error, subprocess, time, datetime, configparser
config=json.loads(pathlib.Path("/home/liteware/.config/video-platform/v2-production.json").read_text())
base="http://127.0.0.1:5082"
def request(method,path,body=None,token=None):
    headers={"Content-Type":"application/json"}
    if token:headers["Authorization"]="Bearer "+token
    req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else (b"" if method=="POST" else None),headers=headers,method=method)
    try:
        with urllib.request.urlopen(req,timeout=70) as res:
            data=res.read()
            return json.loads(data) if data else None
    except urllib.error.HTTPError as ex:
        print("HTTP错误",method,path,ex.code,ex.read().decode())
        raise
def login():
    return request("POST","/api/v2/auth/login",{"username":"admin","password":config["adminPassword"],"clientType":"desktop","clientVersion":"2.0.0-check"})["accessToken"]
'''
    scripts = {
        "access": '''
import concurrent.futures, threading
media_field="httpTsUrl" if transport=="ts" else "httpFlvUrl"
admin=login()
stamp=str(int(time.time()))
role=request("POST","/api/v2/roles",{"name":"实机撤权验证"+stamp,"code":"verify"+stamp,"status":"active","permissionCodes":["live.view","channel.read"]},admin)
user=request("POST","/api/v2/users",{"username":"verify"+stamp,"password":"Verification-2026!"+stamp,"displayName":"实机撤权验证","phone":"","status":"active","roleIds":[role["id"]]},admin)
channel=next(x for x in request("GET","/api/v2/channels?pageSize=200",token=admin)["items"] if x["deviceChannel"]==1)
request("PUT",f"/api/v2/scopes/user/{user['id']}",{"allChannels":False,"scopes":[{"type":"channel","id":channel["id"]}]},admin)
viewer=request("POST","/api/v2/auth/login",{"username":user["username"],"password":"Verification-2026!"+stamp,"clientType":"desktop","clientVersion":"2.0.0-verification"})["accessToken"]
grants=[]
streams=[]
try:
    for credential in [viewer,admin]:
        grant=request("POST","/api/v2/live-sessions",{"channelId":channel["id"],"streamType":2,"profile":"native"},credential)
        grants.append((grant,credential))
        url="http://127.0.0.1:18082/"+grant[media_field].split("/media/",1)[1]
        streams.append(urllib.request.urlopen(url,timeout=8))
    print("媒体格式",transport)
    print("共享上游",grants[0][0][media_field].split("?")[0]==grants[1][0][media_field].split("?")[0])
    print("两个观看者均有数据",all(len(stream.read(4096))>0 for stream in streams))
    request("PUT",f"/api/v2/scopes/user/{user['id']}",{"allChannels":False,"scopes":[]},admin)
    deadline=time.monotonic()+8
    ended=False
    try:
        while time.monotonic()<deadline:
            if not streams[0].read(4096): ended=True;break
    except (OSError,ValueError): ended=True
    print("撤权后现有连接结束",ended)
    assert ended,"撤权后媒体连接未及时终止"
    print("合法观看者继续收流",len(streams[1].read(4096))>0)
    request("DELETE","/api/v2/live-sessions/"+grants[0][0]["id"],token=viewer)
    request("DELETE","/api/v2/live-sessions/"+grants[0][0]["id"],token=viewer)
    print("重复停止通过")
finally:
    for stream in streams:stream.close()
    for grant,credential in grants:
        request("DELETE","/api/v2/live-sessions/"+grant["id"],token=credential)
    request("PUT",f"/api/v2/users/{user['id']}",{"username":user["username"],"displayName":"实机验证已结束","phone":"","status":"disabled","roleIds":[]},admin)
    request("POST","/api/v2/auth/logout",token=admin)
''',
        "health": '''
for name in ["api","worker","adapter","zlm"]:
    print(name,subprocess.run(["systemctl","is-active","video-platform-v2-"+name],capture_output=True,text=True).stdout.strip())
for i in range(20):
    try: print(request("GET","/health"));break
    except Exception: time.sleep(1)
token=login()
print("dashboard",request("GET","/api/v2/dashboard",token=token))
print("system",request("GET","/api/v2/system",token=token))
request("POST","/api/v2/auth/logout",token=token)
''',
        "device": '''
token=login()
env={}
for line in pathlib.Path("/home/liteware/.config/video-platform/hikvision-adapter.env").read_text().splitlines():
    if line and not line.startswith("#") and "=" in line:
        k,v=line.split("=",1);env[k]=v.strip().strip('"').strip("'")
devices=request("GET","/api/v2/devices",token=token)["items"]
if not devices:
    added=request("POST","/api/v2/devices",{"name":"视枢中央录像机","host":env["HIK_DEVICE_IP"],"port":int(env.get("HIK_DEVICE_PORT","8000")),"username":env["HIK_DEVICE_USER"],"password":env["HIK_DEVICE_PASSWORD"],"enabled":True},token)
    device_id=added["id"]
else: device_id=devices[0]["id"]
print("sync",request("POST",f"/api/v2/devices/{device_id}/sync",token=token))
channels=request("GET",f"/api/v2/channels?deviceId={device_id}&pageSize=200",token=token)
print("channels",channels["total"],"online",sum(x["status"]=="online" for x in channels["items"]))
print("PTZ channels",[x["deviceChannel"] for x in channels["items"] if x["ptzCapable"]])
request("POST","/api/v2/auth/logout",token=token)
''',
        "media": '''
token=login()
channels=request("GET","/api/v2/channels?pageSize=200",token=token)["items"]
targets=[x for x in channels if x["status"]=="online" and x["deviceChannel"] in(1,8,81)]
for channel in targets:
    session=None
    try:
        session=request("POST","/api/v2/live-sessions",{"channelId":channel["id"],"streamType":2,"profile":"browser"},token)
        url="http://127.0.0.1:18082/"+session["httpFlvUrl"].split("/media/",1)[1]
        with urllib.request.urlopen(url,timeout=20) as stream:
            sample=stream.read(65536)
        print("live",channel["deviceChannel"],"codec",session.get("codec"),"transcoded",session.get("transcoded"),"bytes",len(sample),"flv",sample[:3]==b"FLV")
    finally:
        if session:request("DELETE","/api/v2/live-sessions/"+session["id"],token=token)
channel=next(x for x in channels if x["deviceChannel"]==8)
end=datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(minutes=10)
start=end-datetime.timedelta(minutes=2)
range_body={"channelId":channel["id"],"start":start.isoformat(),"end":end.isoformat()}
recordings=request("POST","/api/v2/recordings/search",range_body,token)
print("recordings",len(recordings))
playback=None
try:
    playback=request("POST","/api/v2/playback-sessions",dict(range_body,profile="browser"),token)
    for i in range(30):
        time.sleep(1)
        playback=request("GET","/api/v2/playback-sessions/"+playback["id"],token=token)
        if playback["state"] not in("starting",):break
    print("playback",{k:playback.get(k) for k in("state","codec","transcoded","progress","currentTime")})
    url="http://127.0.0.1:18082/"+playback["httpFlvUrl"].split("/media/",1)[1]
    with urllib.request.urlopen(url,timeout=30) as stream:sample=stream.read(65536)
    print("playback_bytes",len(sample),"flv",sample[:3]==b"FLV")
    for body in [{"action":"pause"},{"action":"resume"},{"action":"speed","speed":2},{"action":"seek","position":(start+datetime.timedelta(seconds=30)).isoformat()}]:
        result=request("POST","/api/v2/playback-sessions/"+playback["id"]+"/control",body,token)
        print("control",body["action"],result.get("state"),result.get("speed"))
finally:
    if playback:request("DELETE","/api/v2/playback-sessions/"+playback["id"],token=token)
job=request("POST","/api/v2/exports",range_body,token)
for i in range(60):
    time.sleep(2)
    jobs=request("GET","/api/v2/exports",token=token)["items"]
    state=next(x for x in jobs if x["id"]==job["id"])
    if state["state"] in("completed","failed","cancelled"):break
print("export",{k:state.get(k) for k in("state","progress","error","fileName","fileSize")})
request("POST","/api/v2/auth/logout",token=token)
''',
        "logs": r'''
import json, pathlib, re, subprocess, urllib.parse
hidden="[已隐藏]"
secrets=set()
config_dir=pathlib.Path("/home/liteware/.config/video-platform")
try:
    secrets.update(value for value in json.loads((config_dir/"v2-production.json").read_text()).values() if isinstance(value,str) and value)
except (OSError,ValueError):
    raise RuntimeError("无法读取脱敏配置，日志读取已停止") from None
for filename in ("v2.env","hikvision-adapter.env","platform-api.env"):
    path=config_dir/filename
    if not path.exists():continue
    try:
        for line in path.read_text().splitlines():
            if not line.strip() or line.lstrip().startswith("#") or "=" not in line:continue
            key,value=line.split("=",1)
            if re.search(r"(?i)password|secret|token|key|database_url",key):
                value=value.strip().strip('"').strip("'")
                value=re.sub(r'\\([\\"$`])',r'\1',value)
                if value:secrets.add(value)
    except OSError:
        raise RuntimeError("无法读取脱敏环境文件，日志读取已停止") from None
encoded={variant for value in secrets for variant in (value,urllib.parse.quote(value,safe=""),urllib.parse.quote_plus(value),json.dumps(value,ensure_ascii=False)[1:-1])}
sensitive=re.compile(r"""(?i)((?:["']?)(?:[a-z0-9_-]*(?:token|key|secret|password|pwd)|signature|ticket|authorization|connectionstring)["']?\s*[:=]\s*)(?:"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|[^\s&;,}]+)""")
def redact(text):
    for value in sorted(encoded,key=len,reverse=True):text=text.replace(value,hidden)
    text=re.sub(r"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]+","Bearer "+hidden,text)
    text=re.sub(r"""(?i)\b([a-z][a-z0-9+.-]*://)[^/\s@"']+@""",lambda match:match[1]+hidden+"@",text)
    text=re.sub(r"(?im)((?:set-cookie|cookie|authorization)\s*:\s*)[^\r\n]+",lambda match:match[1]+hidden,text)
    return sensitive.sub(lambda match:match[1]+hidden,text)
def current(unit):
    result=subprocess.run(["systemctl","show",unit,"--property=ActiveState,InvocationID,MainPID,ExecMainStartTimestamp"],capture_output=True,text=True,timeout=15)
    if result.returncode:raise RuntimeError("无法读取服务当前运行实例")
    return dict(line.split("=",1) for line in result.stdout.splitlines() if "=" in line)
for name in ["api","worker","adapter","zlm"]:
    unit="video-platform-v2-"+name+".service"
    for attempt in range(2):
        state=current(unit)
        invocation=state.get("InvocationID","")
        if state.get("ActiveState")!="active" or state.get("MainPID","0")=="0" or not re.fullmatch(r"[a-fA-F0-9]{32}",invocation):
            print("服务",name,"：无正在运行的进程，未读取历史日志")
            break
        command=["journalctl","-b","-u",unit,"_SYSTEMD_INVOCATION_ID="+invocation,"-n","45","--no-pager","-o","json"]
        if state.get("ExecMainStartTimestamp"):command.extend(["--since",state["ExecMainStartTimestamp"]])
        result=subprocess.run(command,capture_output=True,text=True,timeout=20)
        if result.returncode:raise RuntimeError("读取当前运行实例日志失败")
        if current(unit)!=state:
            if attempt==1:print("服务",name,"：读取期间发生重启，本次未输出日志")
            continue
        messages=[]
        for line in result.stdout.splitlines():
            entry=json.loads(line)
            if entry.get("_SYSTEMD_INVOCATION_ID")!=invocation:continue
            message=entry.get("MESSAGE","")
            if isinstance(message,list):message=bytes(message).decode("utf-8","replace")
            messages.append(str(message))
        text=redact("\n".join(messages))
        print("服务",name,"：当前进程启动以来日志\n",text[-12000:] if text else "无日志")
        break
''',
        "switch": '''
import base64, sys
sys.argv=INSTALLER_ARGUMENTS
exec(compile(base64.b64decode("INSTALLER_SOURCE"),"v2-install.py","exec"),{"__name__":"__main__"})
''',
        "publish": '''
print("发布请通过构建产物上传工作流执行")
'''
    }
    scripts["rollback"] = scripts["switch"]
    source = scripts[args.check]
    if args.check in ("switch", "rollback"):
        installer = base64.b64encode((ROOT / "deploy/v2-install.py").read_bytes()).decode()
        arguments = ["v2-install.py", args.check] + (["--snapshot", args.snapshot] if args.snapshot else [])
        source = source.replace("INSTALLER_ARGUMENTS", repr(arguments)).replace("INSTALLER_SOURCE", installer)
    elif args.check != "logs":
        source = prefix + ("\ntransport=" + repr(args.transport) + "\n" if args.check == "access" else "") + source
    script = base64.b64encode(source.encode()).decode()
    try:
        command = "sudo -S -p '' python3 -u -c \"import base64,sys;sys.excepthook=lambda t,v,b: print('验证失败：'+str(v),file=sys.stderr);exec(base64.b64decode('" + script + "'))\""
        stdin, stdout, stderr = client.exec_command(command, timeout=300)
        stdin.write(os.environ["VIDEO_PLATFORM_SSH_PASSWORD"] + "\n"); stdin.flush(); stdin.channel.shutdown_write()
        for line in stdout: print(line.rstrip(), flush=True)
        error=stderr.read().decode("utf-8","replace")
        if error.strip(): print(error[-8000:], flush=True)
        if stdout.channel.recv_exit_status(): raise SystemExit(1)
    finally: client.close()


if __name__ == "__main__": main()
