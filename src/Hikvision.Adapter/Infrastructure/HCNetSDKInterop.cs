using System.Runtime.InteropServices;

internal static partial class HCNetSDKInterop
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct Time
    {
        public uint Year;
        public uint Month;
        public uint Day;
        public uint Hour;
        public uint Minute;
        public uint Second;

        public static Time From(DateTimeOffset value)
        {
            var local = value.ToOffset(TimeSpan.FromHours(8));
            return new Time
            {
                Year = (uint)local.Year,
                Month = (uint)local.Month,
                Day = (uint)local.Day,
                Hour = (uint)local.Hour,
                Minute = (uint)local.Minute,
                Second = (uint)local.Second
            };
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StreamInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Id;
        public uint Channel;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct VodPara
    {
        public uint Size;
        public StreamInfo StreamInfo;
        public Time BeginTime;
        public Time EndTime;
        public uint Window;
        public byte DrawFrame;
        public byte VolumeType;
        public byte VolumeNumber;
        public byte StreamType;
        public uint FileIndex;
        public byte AudioFile;
        public byte CourseFile;
        public byte Download;
        public byte OptimalStreamType;
        public byte Async;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SdkPath
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] Path;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct UserLoginInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 129)] public byte[] DeviceAddress;
        public byte UseTransport;
        public ushort Port;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Username;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] Password;
        public IntPtr LoginCallback;
        public IntPtr User;
        public uint UseAsyncLogin;
        public byte ProxyType;
        public byte UseUtcTime;
        public byte LoginMode;
        public byte Https;
        public uint ProxyId;
        public byte VerifyMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 119)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct PreviewInfo
    {
        public uint Channel;
        public uint StreamType;
        public uint LinkMode;
        public uint PlayWindow;
        public uint Blocked;
        public uint PassbackRecord;
        public byte PreviewMode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] StreamId;
        public byte ProtocolType;
        public byte Reserved1;
        public byte VideoCodingType;
        public uint DisplayBufferCount;
        public byte NpqMode;
        public byte ReceiveMetadata;
        public byte DataType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 213)] public byte[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct SetupAlarmParam
    {
        public uint Size;
        public byte Level;
        public byte AlarmInfoType;
        public byte RetAlarmTypeV40;
        public byte RetDevInfoVersion;
        public byte RetVqdAlarmType;
        public byte FaceAlarmDetection;
        public byte Support;
        public byte BrokenNetHttp;
        public ushort TaskNo;
        public byte DeployType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)] public byte[] Reserved1;
        public byte AlarmTypeUrl;
        public byte CustomCtrl;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void RealDataCallback(int handle, uint dataType, IntPtr buffer, uint size, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int MessageCallback(int command, IntPtr alarmer, IntPtr alarmInfo, uint bufferLength, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ExceptionCallback(uint type, int userId, int handle, IntPtr user);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void PlayDataCallback(int handle, uint dataType, IntPtr buffer, uint size, IntPtr user);

    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_SetSDKInitCfg(int type, IntPtr buffer);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_Init();
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_Cleanup();
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_Login_V40(IntPtr loginInfo, IntPtr deviceInfo);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_Logout(int userId);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern uint NET_DVR_GetLastError();
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_GetDVRConfig(int userId, int command, int channel, IntPtr output, int outputSize, ref uint returned);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_RealPlay_V40(int userId, ref PreviewInfo preview, RealDataCallback callback, IntPtr user);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_StopRealPlay(int handle);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_PTZControlWithSpeed_Other(int userId, int channel, uint command, uint stop, uint speed);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_PTZPreset_Other(int userId, int channel, uint command, uint preset);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_SetDVRMessageCallBack_V31(MessageCallback callback, IntPtr user);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_SetExceptionCallBack_V30(uint reserved1, IntPtr reserved2, ExceptionCallback callback, IntPtr user);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_SetupAlarmChan_V41(int userId, ref SetupAlarmParam parameter);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_CloseAlarmChan_V30(int alarmHandle);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindFile_V50(int userId, IntPtr condition);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindFile_V40(int userId, IntPtr condition);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindFile_V30(int userId, IntPtr condition);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindNextFile_V50(int findHandle, IntPtr data);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindNextFile_V40(int findHandle, IntPtr data);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindNextFile_V30(int findHandle, IntPtr data);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_FindClose_V30(int findHandle);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_PlayBackByTime_V40(int userId, ref VodPara vodPara);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_SetPlayDataCallBack_V40(int playHandle, PlayDataCallback callback, IntPtr user);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_PlayBackControl_V40(int playHandle, uint controlCode, IntPtr input, uint inputLength, IntPtr output, IntPtr outputLength);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_PlayBackControl(int playHandle, uint controlCode, uint input, ref int output);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_StopPlayBack(int playHandle);
}
