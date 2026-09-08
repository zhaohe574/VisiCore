using System.Runtime.InteropServices;

internal static partial class HCNetSDKInterop
{
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_SetReconnect(uint interval, int enabled);
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct DownloadCondition
    {
        public uint Channel;
        public Time Start;
        public Time End;
        public byte DrawFrame;
        public byte StreamType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] StreamId;
        public byte CourseFile;
        public byte Download;
        public byte OptimalStreamType;
        public byte VodFileType;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 26)] public byte[] Reserved;
    }

    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_GetPlayBackOsdTime(int handle, ref Time time);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_GetPlayBackPos(int handle);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_GetFileByTime_V40(int userId, [MarshalAs(UnmanagedType.LPUTF8Str)] string file, ref DownloadCondition condition);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_GetDownloadPos(int handle);
    [DllImport("libhcnetsdk.so", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int NET_DVR_StopGetFile(int handle);
}
