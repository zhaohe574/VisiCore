"""独立只读验证候选录像检索，设备凭据只在服务器内存使用。"""
import base64
import os
import sys
import paramiko

native = r'''
import ctypes as c, json, pathlib, datetime, time, struct, os
values={}
for line in pathlib.Path('/home/liteware/.config/video-platform/hikvision-adapter.env').read_text().splitlines():
    if line and not line.startswith('#') and '=' in line:
        k,v=line.split('=',1); values[k]=v.strip().strip('"').strip("'")
root='/opt/video-platform/sdk/hikvision'
sdk=c.CDLL(root+'/libhcnetsdk.so')
sdk.NET_DVR_SetSDKInitCfg.argtypes=[c.c_int,c.c_void_p]
sdk.NET_DVR_Login_V40.argtypes=[c.c_void_p,c.c_void_p]
for name in ('NET_DVR_FindFile_V50','NET_DVR_FindFile_V40','NET_DVR_FindFile_V30','NET_DVR_FindNextFile_V50','NET_DVR_FindNextFile_V40','NET_DVR_FindNextFile_V30'):
    getattr(sdk,name).argtypes=[c.c_int,c.c_void_p]
sdk.NET_DVR_SetSDKInitCfg(2,c.create_string_buffer(root.encode(),384))
for index,name in ((3,'libcrypto.so.3'),(4,'libssl.so.3')):
    sdk.NET_DVR_SetSDKInitCfg(index,c.create_string_buffer((root+'/'+name).encode()))
if not sdk.NET_DVR_Init(): raise RuntimeError('SDK 初始化失败')
user=-1
try:
    login=c.create_string_buffer(416)
    c.memmove(c.addressof(login),values['HIK_DEVICE_IP'].encode(),len(values['HIK_DEVICE_IP'].encode()))
    struct.pack_into('<H',login,130,int(values.get('HIK_DEVICE_PORT','8000')))
    for offset,name in ((132,'HIK_DEVICE_USER'),(196,'HIK_DEVICE_PASSWORD')):
        raw=values[name].encode(); c.memmove(c.addressof(login)+offset,raw,len(raw))
    info=c.create_string_buffer(344)
    user=sdk.NET_DVR_Login_V40(login,info)
    print(json.dumps({'loginSucceeded':user>=0,'sdkError':sdk.NET_DVR_GetLastError() if user<0 else 0}))
    if user<0: raise RuntimeError('只读 SDK 登录失败')
    end=datetime.datetime.now(datetime.timezone(datetime.timedelta(hours=8)))-datetime.timedelta(minutes=10)
    start=end-datetime.timedelta(minutes=2)
    if os.environ.get('ADAPTER_RANGE_START'):
        start=datetime.datetime.fromisoformat(os.environ['ADAPTER_RANGE_START']).astimezone(datetime.timezone(datetime.timedelta(hours=8)))
        end=datetime.datetime.fromisoformat(os.environ['ADAPTER_RANGE_END']).astimezone(datetime.timezone(datetime.timedelta(hours=8)))
    print('录像范围',start.isoformat(),end.isoformat())
    for version,filetype in ((50,0xffffffff),(50,0xff),(40,0xffffffff),(40,0xff),(30,0xff)):
        condition=c.create_string_buffer({50:424,40:160,30:96}[version])
        if version==50:
            struct.pack_into('<II',condition,0,72,0)
            struct.pack_into('<I',condition,36,8)
            for offset,value in ((72,start),(84,end)):
                struct.pack_into('<HBBBBBBHbb',condition,offset,value.year,value.month,value.day,value.hour,value.minute,value.second,0,0,8,0)
            struct.pack_into('<I',condition,100,filetype)
            struct.pack_into('<B',condition,108,0xff)
            struct.pack_into('<I',condition,168,5000)
        else:
            struct.pack_into('<III',condition,0,8,filetype,0xff)
            for offset,value in ((48,start),(72,end)):
                struct.pack_into('<IIIIII',condition,offset,value.year,value.month,value.day,value.hour,value.minute,value.second)
        handle=getattr(sdk,'NET_DVR_FindFile_V'+str(version))(user,condition)
        if handle<0:
            print(json.dumps({'version':version,'fileType':hex(filetype),'findHandle':handle,'sdkError':sdk.NET_DVR_GetLastError()}));continue
        try:
            found=0;state=None
            data=c.create_string_buffer({50:572,40:320,30:188}[version])
            deadline=time.monotonic()+20
            while time.monotonic()<deadline:
                state=getattr(sdk,'NET_DVR_FindNextFile_V'+str(version))(handle,data)
                if state==1000:found+=1;continue
                if state==1002:time.sleep(.05);continue
                break
            print(json.dumps({'version':version,'fileType':hex(filetype),'found':found,'state':state,'sdkError':sdk.NET_DVR_GetLastError() if state not in(1001,1003) else 0}))
        finally:sdk.NET_DVR_FindClose_V30(handle)
    if os.environ.get('ADAPTER_NATIVE_MEDIA_CHECK')=='1':
        import subprocess,tempfile
        sdk.NET_DVR_PlayBackByTime_V40.argtypes=[c.c_int,c.c_void_p]
        sdk.NET_DVR_SetPlayDataCallBack_V40.argtypes=[c.c_int,c.c_void_p,c.c_void_p]
        sdk.NET_DVR_PlayBackControl_V40.argtypes=[c.c_int,c.c_uint,c.c_void_p,c.c_uint,c.c_void_p,c.c_void_p]
        sdk.NET_DVR_GetPlayBackOsdTime.argtypes=[c.c_int,c.c_void_p]
        vod=c.create_string_buffer(160)
        struct.pack_into('<II',vod,0,160,72);struct.pack_into('<I',vod,40,8)
        finish=start+datetime.timedelta(seconds=12 if os.environ.get('ADAPTER_NATIVE_FLOW_CHECK')!='1' else 120)
        if os.environ.get('ADAPTER_RANGE_START'):finish=end
        for offset,value in ((76,start),(100,finish)):
            struct.pack_into('<IIIIII',vod,offset,value.year,value.month,value.day,value.hour,value.minute,value.second)
        struct.pack_into('<B',vod,140,1)
        stats={'bytes':0,'types':{}}
        playback_file=None
        playback_stream=None
        if os.environ.get('ADAPTER_EXPORT_CALLBACK')=='1':
            playback_file=pathlib.Path('/var/lib/video-platform/v2/exports')/'callback-review.ps'
            playback_stream=playback_file.open('wb')
        @c.CFUNCTYPE(None,c.c_int,c.c_uint,c.c_void_p,c.c_uint,c.c_void_p)
        def callback(handle,kind,data,size,context):
            stats['bytes']+=size;stats['types'][str(kind)]=stats['types'].get(str(kind),0)+1
            if playback_stream is not None and kind in (1,2) and data and size:
                playback_stream.write(c.string_at(data,size))
        playback=sdk.NET_DVR_PlayBackByTime_V40(user,vod)
        print('playback_create',json.dumps({'success':playback>=0,'sdkError':sdk.NET_DVR_GetLastError() if playback<0 else 0}))
        if playback>=0:
            try:
                print('playback_callback',sdk.NET_DVR_SetPlayDataCallBack_V40(playback,callback,None))
                print('playback_start',sdk.NET_DVR_PlayBackControl_V40(playback,1,None,0,None,None))
                if os.environ.get('ADAPTER_NATIVE_FLOW_CHECK')=='1':
                    waiting=time.monotonic()+3
                    while stats['bytes']<4*1024*1024 and time.monotonic()<waiting:time.sleep(.005)
                    before=stats['bytes'];paused=sdk.NET_DVR_PlayBackControl_V40(playback,3,None,0,None,None)
                    time.sleep(1);after=stats['bytes']
                    transfer=sdk.NET_DVR_GetPlayBackPos(playback)
                    print('playback_flow_pause',json.dumps({'before':before,'after':after,'success':bool(paused),'sdkPosition':transfer}))
                    sdk.NET_DVR_PlayBackControl_V40(playback,4,None,0,None,None)
                time.sleep(3)
                osd=c.create_string_buffer(24)
                ok=sdk.NET_DVR_GetPlayBackOsdTime(playback,osd)
                print('playback_osd',json.dumps({'success':bool(ok),'time':struct.unpack('<IIIIII',osd),'sdkError':sdk.NET_DVR_GetLastError() if not ok else 0}))
                for query in (13,14,17):
                    value=c.c_uint(0);length=c.c_uint(0)
                    ok=sdk.NET_DVR_PlayBackControl_V40(playback,query,None,0,c.byref(value),c.byref(length))
                    print('playback_query',json.dumps({'command':query,'success':bool(ok),'value':value.value,'length':length.value,'sdkError':sdk.NET_DVR_GetLastError() if not ok else 0}))
                for command in (3,4,7,5):
                    ok=sdk.NET_DVR_PlayBackControl_V40(playback,command,None,0,None,None)
                    print('playback_control',json.dumps({'command':command,'success':bool(ok),'sdkError':sdk.NET_DVR_GetLastError() if not ok else 0}));time.sleep(.25)
                time.sleep(2)
                print('playback_data',json.dumps(stats))
            finally:
                sdk.NET_DVR_StopPlayBack(playback)
                if playback_stream is not None: playback_stream.close()
                if playback_file is not None and playback_file.exists():
                    probe=subprocess.run(['/usr/bin/ffprobe','-v','error','-show_entries','stream=codec_name,codec_type,width,height:format=duration,format_name','-of','json',str(playback_file)],capture_output=True,text=True,timeout=15)
                    print('callback_codecs',probe.stdout,probe.stderr[:1000])
                    print('callback_header',playback_file.open('rb').read(64).hex())
        sdk.NET_DVR_GetFileByTime_V40.argtypes=[c.c_int,c.c_char_p,c.c_void_p]
        condition=c.create_string_buffer(116)
        struct.pack_into('<I',condition,0,8)
        for offset,value in ((4,start),(28,finish)):
            struct.pack_into('<IIIIII',condition,offset,value.year,value.month,value.day,value.hour,value.minute,value.second)
        struct.pack_into('<B',condition,87,1)
        with tempfile.TemporaryDirectory(prefix='adapter-review-',dir='/var/lib/video-platform/v2/exports') as work:
            path=pathlib.Path(work)/'review.download'
            download=sdk.NET_DVR_GetFileByTime_V40(user,str(path).encode(),condition)
            print('download_create',json.dumps({'success':download>=0,'sdkError':sdk.NET_DVR_GetLastError() if download<0 else 0}))
            if download>=0:
                try:
                    sdk.NET_DVR_PlayBackControl_V40(download,1,None,0,None,None)
                    deadline=time.monotonic()+30;position=-1
                    while time.monotonic()<deadline:
                        position=sdk.NET_DVR_GetDownloadPos(download)
                        if position<0 or position>=100:break
                        time.sleep(.25)
                    print('download_complete',json.dumps({'progress':position,'bytes':path.stat().st_size if path.exists() else 0}))
                finally:sdk.NET_DVR_StopGetFile(download)
                if path.exists() and path.stat().st_size:
                    probe=subprocess.run(['/usr/bin/ffprobe','-v','error','-show_entries','stream=index,codec_name,codec_type,width,height,extradata_size:format=duration,format_name','-of','json',str(path)],capture_output=True,text=True,timeout=15)
                    print('download_codecs',probe.stdout)
                    print('download_header',path.open('rb').read(64).hex())
                    with path.open('rb') as sample_file:sample=sample_file.read(4*1024*1024)
                    print('download_start_codes', {code: {'count':sample.count(bytes.fromhex(code)), 'first':sample.find(bytes.fromhex(code))} for code in ('000001ba','000001e0','0000000167','000000014001','000000014201')})
                    ps_offset=sample.find(bytes.fromhex('000001ba'))
                    if ps_offset > 0:
                        fixed=path.with_suffix('.fixed.ps')
                        with path.open('rb') as source, fixed.open('wb') as destination:
                            source.seek(ps_offset)
                            while chunk:=source.read(1024*1024): destination.write(chunk)
                        fixed_probe=subprocess.run(['/usr/bin/ffprobe','-v','error','-show_entries','stream=codec_name,codec_type,width,height:format=duration,format_name','-of','json',str(fixed)],capture_output=True,text=True,timeout=20)
                        print('download_fixed',ps_offset,fixed_probe.stdout,fixed_probe.stderr[:1000])
                        fixed_mp4=fixed.with_suffix('.mp4')
                        fixed_remux=subprocess.run(['/usr/bin/ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(fixed),'-map','0:v:0','-c:v','copy',str(fixed_mp4)],capture_output=True,text=True,timeout=30)
                        print('download_fixed_remux',fixed_remux.returncode,fixed_remux.stderr[:4000])
                    forced=subprocess.run(['/usr/bin/ffprobe','-v','error','-c:v','hevc','-show_entries','stream=codec_name,width,height','-of','json',str(path)],capture_output=True,text=True,timeout=20)
                    print('download_forced_hevc',forced.stdout,forced.stderr[:1000])
                    remux=subprocess.run(['/usr/bin/ffmpeg','-hide_banner','-loglevel','error','-y','-i',str(path),'-map','0:v:0','-c:v','copy',str(path.with_suffix('.mp4'))],capture_output=True,text=True,timeout=30)
                    print('download_remux',remux.returncode,remux.stderr[:4000])
finally:
    if user>=0:sdk.NET_DVR_Logout(user)
    sdk.NET_DVR_Cleanup()
'''

wrapper = """
import subprocess,os,base64,sys
env=dict(os.environ,LD_LIBRARY_PATH='/opt/video-platform/sdk/hikvision',ADAPTER_NATIVE_MEDIA_CHECK='%s',ADAPTER_NATIVE_FLOW_CHECK='%s',ADAPTER_RANGE_START=%r,ADAPTER_RANGE_END=%r)
result=subprocess.run([sys.executable,'-u','-c',base64.b64decode('%s').decode()],env=env,capture_output=True,text=True,timeout=150)
print(result.stdout)
if result.returncode:print('独立 SDK 检索失败，退出码：',result.returncode)
""" % ('1' if '--media' in sys.argv else '0', '1' if '--flow' in sys.argv else '0', os.environ.get('ADAPTER_RANGE_START',''), os.environ.get('ADAPTER_RANGE_END',''), base64.b64encode(native.encode()).decode())

client = paramiko.SSHClient()
client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
try:
    client.connect('10.37.200.74', username='liteware', password=os.environ['VIDEO_PLATFORM_SSH_PASSWORD'], timeout=15, look_for_keys=False, allow_agent=False)
    encoded = base64.b64encode(wrapper.encode()).decode()
    stdin, stdout, stderr = client.exec_command("python3 -u -c \"import base64;exec(base64.b64decode('"+encoded+"'))\"", timeout=180)
    for line in stdout: print(line.rstrip(), flush=True)
    if stdout.channel.recv_exit_status(): print('远端诊断执行失败。')
finally:
    client.close()
