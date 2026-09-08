using System.Net;
using System.Text;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using Native = HCNetSDKInterop;

internal sealed partial class HikvisionDevice
{
    private int Login(out byte[] deviceInfo)
    {
        var login = new Native.UserLoginInfo
        {
            DeviceAddress = EncodeFixed(_options.DeviceIp, 129),
            Port = _options.DevicePort,
            Username = EncodeFixed(_options.Username, 64),
            Password = EncodeFixed(_options.Password, 64),
            LoginCallback = IntPtr.Zero,
            User = IntPtr.Zero,
            UseAsyncLogin = 0,
            ProxyType = 0,
            UseUtcTime = 0,
            LoginMode = 0,
            Https = 0,
            ProxyId = 0,
            VerifyMode = 0,
            Reserved = new byte[119]
        };
        var loginPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Native.UserLoginInfo>());
        var devicePtr = Marshal.AllocHGlobal(344);
        try
        {
            Zero(loginPtr, Marshal.SizeOf<Native.UserLoginInfo>());
            Marshal.StructureToPtr(login, loginPtr, false);
            Zero(devicePtr, 344);
            var userId = Native.NET_DVR_Login_V40(loginPtr, devicePtr);
            if (userId < 0)
                throw SdkError("设备登录失败");
            deviceInfo = new byte[344];
            Marshal.Copy(devicePtr, deviceInfo, 0, deviceInfo.Length);
            return userId;
        }
        finally
        {
            Marshal.FreeHGlobal(loginPtr);
            Marshal.FreeHGlobal(devicePtr);
        }
    }

    private DeviceSummary Summarize(byte[] data)
    {
        var serial = Decode(data, 0, 48);
        var analogChannels = data[52];
        var digitalChannels = data[55] + (data[68] << 8);
        return new DeviceSummary(
            serial,
            analogChannels,
            digitalChannels,
            data[66],
            data[50],
            data[48],
            data[49],
            data[57] is 1 or 2 || data[58] is 1 or 2,
            null,
            _options.DeviceIp,
            _options.DevicePort);
    }

    private IsapiDeviceInfo? TryReadIsapiDeviceInfo()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                PreAuthenticate = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync($"http://{_options.DeviceIp}/ISAPI/System/deviceInfo")
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;
            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var root = XDocument.Parse(xml);
            static string? Value(XDocument document, string name) =>
                document.Descendants().FirstOrDefault(item => item.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } value ? value : null;
            return new IsapiDeviceInfo(Value(root, "model"), Value(root, "serialNumber"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Console.Error.WriteLine($"ISAPI 设备信息同步失败：{ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<ChannelSummary>? TryReadIsapiChannels()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                PreAuthenticate = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            using var response = client.GetAsync($"http://{_options.DeviceIp}/ISAPI/ContentMgmt/InputProxy/channels")
                .GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var statusMap = new Dictionary<int, bool>();
            try
            {
                using var statusResponse = client.GetAsync($"http://{_options.DeviceIp}/ISAPI/ContentMgmt/InputProxy/channels/status")
                    .GetAwaiter().GetResult();
                if (statusResponse.IsSuccessStatusCode)
                {
                    var statusXml = statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var statusDoc = XDocument.Parse(statusXml);
                    foreach (var element in statusDoc.Descendants().Where(e => e.Name.LocalName == "InputProxyChannelStatus"))
                    {
                        var idStr = element.Elements().FirstOrDefault(i => i.Name.LocalName == "id")?.Value;
                        if (int.TryParse(idStr, out var id) && id > 0)
                        {
                            var onlineStr = element.Elements().FirstOrDefault(i => i.Name.LocalName == "online")?.Value;
                            var detectResult = element.Elements().FirstOrDefault(i => i.Name.LocalName == "chanDetectResult")?.Value;
                            var isOnline = (bool.TryParse(onlineStr, out var b) && b)
                                || string.Equals(detectResult, "connect", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(detectResult, "online", StringComparison.OrdinalIgnoreCase);
                            statusMap[id] = isOnline;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ISAPI 通道状态读取异常（将回退使用配置中的 online 属性）：{ex.Message}");
            }

            var channels = XDocument.Parse(xml)
                .Descendants()
                .Where(element => element.Name.LocalName == "InputProxyChannel")
                .Select(element =>
                {
                    static string? Value(XElement parent, string name) =>
                        parent.Elements().FirstOrDefault(item => item.Name.LocalName == name)?.Value;

                    var source = element.Elements().FirstOrDefault(item => item.Name.LocalName == "sourceInputPortDescriptor");
                    var rawOnline = Value(element, "online");
                    var onlineFromConfig = bool.TryParse(rawOnline, out var onlineValue) ? onlineValue : (bool?)null;

                    var name = Value(element, "name");
                    var model = source is null ? null : Value(source, "model");
                    var capabilityValue = element.Descendants().FirstOrDefault(item => item.Name.LocalName is "ptz" or "ptzCapable")?.Value;
                    var ptzCapable = bool.TryParse(capabilityValue, out var capability)
                        ? capability
                        : LooksLikePtz(model) || LooksLikePtz(name);

                    var id = int.TryParse(Value(element, "id"), out var parsedId) ? parsedId : 0;
                    var online = statusMap.TryGetValue(id, out var statusOnline)
                        ? statusOnline
                        : onlineFromConfig ?? false;

                    return new ChannelSummary(
                        id,
                        online,
                        0,
                        name,
                        model,
                        online,
                        ptzCapable);
                })
                .Where(channel => channel.ChannelNumber > 0)
                .OrderBy(channel => channel.ChannelNumber)
                .ToArray();
            return channels.Length == 0 ? null : channels;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException)
        {
            Console.Error.WriteLine($"ISAPI 通道同步失败，回退 HCNetSDK：{ex.Message}");
            return null;
        }
    }

    private IReadOnlyList<ChannelSummary> ReadIpChannels(int userId)
    {
        var buffer = Marshal.AllocHGlobal(IpParaConfigSize);
        var returned = 0u;
        try
        {
            Zero(buffer, IpParaConfigSize);
            WriteUInt32(buffer, 0, IpParaConfigSize);
            if (Native.NET_DVR_GetDVRConfig(userId, GetIpParaConfigV40, 0, buffer, IpParaConfigSize, ref returned) == 0)
                throw SdkError("读取 IP 通道配置失败");

            var count = (int)Math.Min(ReadUInt32(buffer, 12), 64);
            var startChannel = ReadUInt32(buffer, 16);
            var channels = new List<ChannelSummary>(count);
            for (var index = 0; index < count; index++)
            {
                var offset = StreamModeOffset + index * StreamModeSize;
                channels.Add(new ChannelSummary(
                    (int)startChannel + index,
                    Marshal.ReadByte(buffer, offset + 4) != 0,
                    Marshal.ReadByte(buffer, offset)));
            }
            return channels;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool LooksLikePtz(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("ptz", StringComparison.OrdinalIgnoreCase)
            || value.Contains("dome", StringComparison.OrdinalIgnoreCase)
            || value.Contains("2dc", StringComparison.OrdinalIgnoreCase)
            || value.Contains("球机", StringComparison.OrdinalIgnoreCase)
            || value.Contains("云台", StringComparison.OrdinalIgnoreCase));

    private static Exception SdkError(string message) =>
        new AdapterException(502, "SDK_ERROR", $"{message}，SDK 错误码：{Native.NET_DVR_GetLastError()}");

    private static byte[] EncodeFixed(string value, int size)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length >= size)
            throw new ArgumentException($"字段长度超过限制：{size - 1} 字节");
        var result = new byte[size];
        bytes.CopyTo(result, 0);
        return result;
    }

    private static string Decode(byte[] data, int offset, int length)
    {
        var end = Array.IndexOf(data, (byte)0, offset, length);
        if (end < 0)
            end = offset + length;
        return Encoding.UTF8.GetString(data, offset, end - offset);
    }

    private static void Zero(IntPtr pointer, int size) =>
        Marshal.Copy(new byte[size], 0, pointer, size);

    private static uint ReadUInt32(IntPtr pointer, int offset) =>
        unchecked((uint)Marshal.ReadInt32(pointer, offset));

    private static string Decode(IntPtr pointer, int offset, int length)
    {
        var bytes = new byte[length];
        Marshal.Copy(IntPtr.Add(pointer, offset), bytes, 0, length);
        var end = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, end < 0 ? length : end).Trim();
    }

    private static void WriteUInt32(IntPtr pointer, int offset, int value) =>
        Marshal.WriteInt32(pointer, offset, value);

}

internal sealed record ProbeResult(
    DeviceSummary Device,
    IReadOnlyList<ChannelSummary> IpChannels,
    PreviewResult? Preview);

internal sealed record DeviceSummary(
    string SerialNumber,
    int AnalogChannels,
    int DigitalChannels,
    int DigitalStartChannel,
    int DiskCount,
    int AlarmInputCount,
    int AlarmOutputCount,
    bool SupportsRtsp,
    string? Model = null,
    string? Ip = null,
    int ServicePort = 8000);

internal sealed record ChannelSummary(
    int ChannelNumber,
    bool Enabled,
    int StreamType,
    string? Name = null,
    string? Model = null,
    bool? Online = null,
    bool PtzCapable = false);

internal sealed record IsapiDeviceInfo(string? Model, string? SerialNumber);

internal sealed record PreviewResult(
    int Channel,
    int Callbacks,
    long Bytes,
    IReadOnlyDictionary<uint, int> DataTypes);
