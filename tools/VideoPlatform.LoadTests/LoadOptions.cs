using System.Net;
using System.Net.Sockets;

namespace VideoPlatform.LoadTests;

public sealed record LoadOptions(string DatabaseUrl, string ApiDll, string Dotnet, string OutputRoot, int DurationSeconds, int ThinkMilliseconds, int ApiPoolSize)
{
    public const int AccountCount = 800;
    public const int ClientCount = 100;
    public const int PlaybackCount = 20;

    public static LoadOptions Read()
    {
        var database = Environment.GetEnvironmentVariable("LOAD_TEST_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(database)) throw new InvalidOperationException("必须设置 LOAD_TEST_DATABASE_URL，工具只在随机独立 schema 内写入测试数据。");
        var api = Environment.GetEnvironmentVariable("LOAD_TEST_API_DLL");
        if (string.IsNullOrWhiteSpace(api) || !File.Exists(api)) throw new InvalidOperationException("必须设置 LOAD_TEST_API_DLL，指向已构建的真实 VideoPlatform.Api.dll。");
        var dotnet = Environment.GetEnvironmentVariable("LOAD_TEST_DOTNET") ?? Environment.ProcessPath ?? "dotnet";
        if (!Path.GetFileNameWithoutExtension(dotnet).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("必须设置 LOAD_TEST_DOTNET，指向 .NET 10 的 dotnet 可执行文件。");
        return new(database, Path.GetFullPath(api), dotnet,
            Path.GetFullPath(Environment.GetEnvironmentVariable("LOAD_TEST_OUTPUT") ?? Path.Combine("tools", "VideoPlatform.LoadTests", "results")),
            Number("LOAD_TEST_DURATION_SECONDS", 30, 5, 300), Number("LOAD_TEST_THINK_MS", 1000, 0, 10000), Number("LOAD_TEST_API_POOL_SIZE", 64, 4, 200));
    }

    private static int Number(string name, int fallback, int min, int max)
        => Environment.GetEnvironmentVariable(name) is not { } text ? fallback : int.TryParse(text, out var number) && number >= min && number <= max
            ? number : throw new InvalidOperationException($"{name} 必须在 {min}～{max} 之间。");

    public static int FreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
