# 设备验证工具

`hikvision_probe.py` 使用海康 Linux SDK 验证 DS-A72124R 的登录、设备信息、IP 通道状态和单路实时码流回调。设备通道清单也可通过 ISAPI 读取，但当前探针只输出 SDK 配置；生产 C# 适配服务已补充 ISAPI 同步。

## 运行

先把 Linux SDK 的 `库文件` 内容放到 SDK 目录，并把 Linux 示例中的 `HCNetSDK.py` 放到探针目录：

```bash
export HIK_SDK_DIR=/opt/video-platform/sdk/hikvision
export HIK_VENDOR_DIR=/opt/video-platform/probe
export HIK_DEVICE_IP=设备IP
export HIK_DEVICE_PORT=8000
export HIK_DEVICE_USER=设备账号
export HIK_DEVICE_PASSWORD=设备密码
python3 /opt/video-platform/probe/hikvision_probe.py
python3 /opt/video-platform/probe/hikvision_probe.py --preview-channel 1 --preview-seconds 5
```

探针不会输出或保存设备密码。`--preview-channel` 一次只验证一路，避免在能力上限未确认前压垮录像机。
