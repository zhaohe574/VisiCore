#!/usr/bin/env python3
# coding: utf-8
"""海康 HCNetSDK Linux 探针。

只负责设备能力验证：初始化 SDK、登录、读取设备与通道信息，并可选地
建立一路实时预览确认码流回调。设备密码只从环境变量读取，不输出也不落盘。
"""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import sys
import time
from pathlib import Path


def read_env(name: str, default: str | None = None) -> str:
    value = os.environ.get(name, default)
    if not value:
        raise SystemExit(f"缺少环境变量：{name}")
    return value


def load_vendor_types(vendor_dir: Path):
    sys.path.insert(0, str(vendor_dir))
    try:
        from HCNetSDK import (  # type: ignore
            NET_DVR_DEVICEINFO_V40,
            NET_DVR_GET_IPPARACFG_V40,
            NET_DVR_IPPARACFG_V40,
            NET_DVR_LOCAL_SDK_PATH,
            NET_DVR_PREVIEWINFO,
            NET_DVR_USER_LOGIN_INFO,
            REALDATACALLBACK,
        )
    except ImportError as exc:
        raise SystemExit(f"无法加载海康 Python 结构体定义：{exc}") from exc
    return (
        NET_DVR_DEVICEINFO_V40,
        NET_DVR_GET_IPPARACFG_V40,
        NET_DVR_IPPARACFG_V40,
        NET_DVR_LOCAL_SDK_PATH,
        NET_DVR_PREVIEWINFO,
        NET_DVR_USER_LOGIN_INFO,
        REALDATACALLBACK,
    )


def c_string(value: bytes) -> str:
    return value.split(b"\0", 1)[0].decode("utf-8", errors="replace")


def configure_sdk(sdk, sdk_dir: Path, sdk_path_type):
    sdk.NET_DVR_SetSDKInitCfg.argtypes = [ctypes.c_int, ctypes.c_void_p]
    sdk.NET_DVR_SetSDKInitCfg.restype = ctypes.c_bool

    component_path = sdk_path_type()
    component_path.sPath = str(sdk_dir).encode("utf-8")
    if not sdk.NET_DVR_SetSDKInitCfg(2, ctypes.byref(component_path)):
        raise RuntimeError("设置 HCNetSDKCom 路径失败")

    for cfg_type, library_name in ((3, "libcrypto.so.3"), (4, "libssl.so.3")):
        library_path = sdk_dir / library_name
        if not library_path.is_file():
            raise RuntimeError(f"缺少 SDK 依赖库：{library_path}")
        library_bytes = ctypes.create_string_buffer(str(library_path).encode("utf-8"))
        if not sdk.NET_DVR_SetSDKInitCfg(cfg_type, library_bytes):
            raise RuntimeError(f"设置 SDK 依赖库路径失败：{library_name}")


def login(sdk, types, ip: str, port: int, username: str, password: str):
    device_info_type, _, _, _, _, login_info_type, _ = types
    login_info = login_info_type()
    login_info.sDeviceAddress = ip.encode("utf-8")
    login_info.wPort = port
    login_info.sUserName = username.encode("utf-8")
    login_info.sPassword = password.encode("utf-8")
    login_info.bUseAsynLogin = 0
    login_info.byLoginMode = 0

    device_info = device_info_type()
    sdk.NET_DVR_Login_V40.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
    sdk.NET_DVR_Login_V40.restype = ctypes.c_int
    user_id = sdk.NET_DVR_Login_V40(ctypes.byref(login_info), ctypes.byref(device_info))
    if user_id < 0:
        raise RuntimeError(f"设备登录失败，错误码：{sdk.NET_DVR_GetLastError()}")
    return user_id, device_info


def device_summary(device_info) -> dict:
    v30 = device_info.struDeviceV30
    digital_channels = int(v30.byIPChanNum) + (int(v30.byHighDChanNum) << 8)
    return {
        "serialNumber": c_string(bytes(v30.sSerialNumber)),
        "analogChannels": int(v30.byChanNum),
        "digitalChannels": digital_channels,
        "digitalStartChannel": int(v30.byStartDChan),
        "diskCount": int(v30.byDiskNum),
        "alarmInputCount": int(v30.byAlarmInPortNum),
        "alarmOutputCount": int(v30.byAlarmOutPortNum),
        "supportsRtsp": bool(int(v30.byMainProto) in (1, 2) or int(v30.bySubProto) in (1, 2)),
    }


def read_ip_channels(sdk, user_id: int, types) -> list[dict]:
    _, config_type, config_struct_type, _, _, _, _ = types
    config = config_struct_type()
    config.dwSize = ctypes.sizeof(config)
    returned = ctypes.c_uint(0)
    sdk.NET_DVR_GetDVRConfig.argtypes = [
        ctypes.c_int,
        ctypes.c_uint,
        ctypes.c_int,
        ctypes.c_void_p,
        ctypes.c_uint,
        ctypes.c_void_p,
    ]
    sdk.NET_DVR_GetDVRConfig.restype = ctypes.c_bool
    if not sdk.NET_DVR_GetDVRConfig(
        user_id,
        config_type,
        0,
        ctypes.byref(config),
        ctypes.sizeof(config),
        ctypes.byref(returned),
    ):
        raise RuntimeError(f"读取 IP 通道配置失败，错误码：{sdk.NET_DVR_GetLastError()}")

    channels: list[dict] = []
    for index in range(min(int(config.dwDChanNum), len(config.struStreamMode))):
        channel_number = int(config.dwStartDChan) + index
        stream_mode = config.struStreamMode[index]
        channels.append(
            {
                "channelNumber": channel_number,
                "enabled": bool(stream_mode.uGetStream.struChanInfo.byEnable),
                "streamType": int(stream_mode.byGetStreamType),
            }
        )
    return channels


def preview_once(sdk, user_id: int, channel: int, duration: float, preview_type, callback_type) -> dict:
    preview = preview_type()
    preview.lChannel = channel
    preview.dwStreamType = 1
    preview.dwLinkMode = 0
    preview.hPlayWnd = 0
    preview.bBlocked = 1

    received = {"callbacks": 0, "bytes": 0, "dataTypes": {}}

    def callback(_handle, data_type, _buffer, size, _user):
        received["callbacks"] += 1
        received["bytes"] += int(size)
        key = str(int(data_type))
        received["dataTypes"][key] = received["dataTypes"].get(key, 0) + 1

    callback_ref = callback_type(callback)
    sdk.NET_DVR_RealPlay_V40.argtypes = [
        ctypes.c_int,
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.c_void_p,
    ]
    sdk.NET_DVR_RealPlay_V40.restype = ctypes.c_int
    handle = sdk.NET_DVR_RealPlay_V40(user_id, ctypes.byref(preview), callback_ref, None)
    if handle < 0:
        raise RuntimeError(f"通道 {channel} 开始预览失败，错误码：{sdk.NET_DVR_GetLastError()}")
    try:
        time.sleep(duration)
    finally:
        sdk.NET_DVR_StopRealPlay.argtypes = [ctypes.c_int]
        sdk.NET_DVR_StopRealPlay.restype = ctypes.c_bool
        sdk.NET_DVR_StopRealPlay(handle)
    return received


def main() -> int:
    parser = argparse.ArgumentParser(description="海康 HCNetSDK Linux 设备探针")
    parser.add_argument("--sdk-dir", default=os.environ.get("HIK_SDK_DIR", "/opt/video-platform/sdk/hikvision"))
    parser.add_argument("--vendor-dir", default=os.environ.get("HIK_VENDOR_DIR", "/opt/video-platform/probe"))
    parser.add_argument("--preview-channel", type=int)
    parser.add_argument("--preview-seconds", type=float, default=5)
    args = parser.parse_args()

    sdk_dir = Path(args.sdk_dir).resolve()
    library = sdk_dir / "libhcnetsdk.so"
    if not library.is_file():
        raise SystemExit(f"找不到 HCNetSDK：{library}")
    types = load_vendor_types(Path(args.vendor_dir).resolve())
    sdk = ctypes.cdll.LoadLibrary(str(library))

    sdk.NET_DVR_GetLastError.restype = ctypes.c_uint
    sdk.NET_DVR_Init.restype = ctypes.c_bool
    sdk.NET_DVR_Cleanup.restype = ctypes.c_bool
    configure_sdk(sdk, sdk_dir, types[3])
    if not sdk.NET_DVR_Init():
        raise SystemExit(f"HCNetSDK 初始化失败，错误码：{sdk.NET_DVR_GetLastError()}")

    user_id = -1
    try:
        ip = read_env("HIK_DEVICE_IP")
        port = int(os.environ.get("HIK_DEVICE_PORT", "8000"))
        username = read_env("HIK_DEVICE_USER")
        password = read_env("HIK_DEVICE_PASSWORD")
        user_id, device_info = login(sdk, types, ip, port, username, password)
        channels = read_ip_channels(sdk, user_id, types)
        result = {"device": device_summary(device_info), "ipChannels": channels}
        if args.preview_channel is not None:
            result["preview"] = preview_once(
                sdk,
                user_id,
                args.preview_channel,
                args.preview_seconds,
                types[4],
                types[6],
            )
        print(json.dumps(result, ensure_ascii=False, indent=2))
        return 0
    finally:
        if user_id >= 0:
            sdk.NET_DVR_Logout.argtypes = [ctypes.c_int]
            sdk.NET_DVR_Logout.restype = ctypes.c_bool
            sdk.NET_DVR_Logout(user_id)
        sdk.NET_DVR_Cleanup()


if __name__ == "__main__":
    main()
