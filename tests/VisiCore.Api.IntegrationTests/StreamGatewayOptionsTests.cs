using Xunit;

namespace VisiCore.Api.IntegrationTests;

public sealed class StreamGatewayOptionsTests
{
    [Fact(DisplayName = "合法的流网关租约和配额配置可以通过校验")]
    public void ValidConfigurationIsAccepted()
    {
        var options = CreateValidOptions();

        Assert.True(options.TryValidate(out var settings, out var error), error);
        Assert.Equal(20, settings.TicketLifetimeSeconds);
        Assert.Equal(120, settings.LeaseLifetimeSeconds);
        Assert.Equal(64, settings.MaxActiveSessionsPerClient);
    }

    [Theory(DisplayName = "弱控制令牌或错误续租间隔会被拒绝")]
    [InlineData("short", 40)]
    [InlineData("integration-control-token-at-least-32-bytes", 110)]
    public void UnsafeConfigurationIsRejected(string controlToken, int renewAfterSeconds)
    {
        var options = CreateValidOptions(controlToken, renewAfterSeconds);

        Assert.False(options.TryValidate(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory(DisplayName = "票据审计保留期超出允许范围会被拒绝")]
    [InlineData(0)]
    [InlineData(169)]
    public void InvalidTicketRetentionIsRejected(int retentionHours)
    {
        var options = CreateValidOptions(ticketRetentionHours: retentionHours);

        Assert.False(options.TryValidate(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Theory(DisplayName = "主动撤销必须使用 HTTPS 和独立高熵命令令牌")]
    [InlineData("http://gateway-control.integration.test/", "integration-command-token-at-least-32-bytes")]
    [InlineData("https://gateway-control.integration.test/", "short")]
    public void UnsafeCommandConfigurationIsRejected(string commandBaseUri, string commandToken)
    {
        var options = CreateValidOptions(commandBaseUri: commandBaseUri, commandToken: commandToken);

        Assert.False(options.TryValidate(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact(DisplayName = "本机开发模式允许流网关使用回环 HTTP")]
    public void LoopbackHttpGatewayIsAcceptedOnlyForExplicitDevelopmentMode()
    {
        var options = CreateValidOptions(
            publicBaseUri: "http://127.0.0.1:15095/",
            commandBaseUri: "http://127.0.0.1:15095/",
            allowInsecureLoopbackHttpForDevelopment: true);

        Assert.True(options.TryValidate(out var settings, out var error), error);
        Assert.Equal("http", settings.PublicBaseUri.Scheme);
        Assert.True(options.TryValidateCommand(out var commandSettings, out error), error);
        Assert.Equal("http", commandSettings.BaseUri.Scheme);
    }

    [Fact(DisplayName = "显式开发模式兼容既有的 24 位网关令牌")]
    public void DevelopmentModeAcceptsExisting24CharacterGatewayTokens()
    {
        var options = CreateValidOptions(
            controlToken: new string('c', 24),
            commandToken: new string('d', 24),
            publicBaseUri: "http://127.0.0.1:15095/",
            commandBaseUri: "http://127.0.0.1:15095/",
            allowInsecureLoopbackHttpForDevelopment: true);

        Assert.True(options.TryValidate(out _, out var error), error);
    }

    [Fact(DisplayName = "开发 HTTP 豁免不能用于非回环地址")]
    public void DevelopmentHttpExceptionDoesNotAcceptNonLoopbackAddress()
    {
        var options = CreateValidOptions(
            publicBaseUri: "http://gateway.integration.test/",
            commandBaseUri: "http://gateway-control.integration.test/",
            allowInsecureLoopbackHttpForDevelopment: true);

        Assert.False(options.TryValidate(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact(DisplayName = "开发模式仅接受显式白名单中的内部流网关命令地址")]
    public void TrustedDevelopmentCommandHostIsAccepted()
    {
        var options = CreateValidOptions(
            publicBaseUri: "http://127.0.0.1:15095/",
            commandBaseUri: "http://mediamtx:15095/",
            allowInsecureLoopbackHttpForDevelopment: true,
            trustedDevelopmentHttpHosts: ["mediamtx"]);

        Assert.True(options.TryValidate(out _, out var error), error);
        Assert.True(options.TryValidateCommand(out var commandSettings, out error), error);
        Assert.Equal("mediamtx", commandSettings.BaseUri.Host);
    }

    [Fact(DisplayName = "主动撤销命令令牌不能与网关控制令牌复用")]
    public void ReusedGatewayTokensAreRejected()
    {
        const string sharedToken = "shared-gateway-token-at-least-32-bytes";
        var options = CreateValidOptions(controlToken: sharedToken, commandToken: sharedToken);

        Assert.False(options.TryValidate(out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact(DisplayName = "主动撤销锁租期必须覆盖 HTTP 投递超时和数据库收尾")]
    public void ShortCommandLockIsRejected()
    {
        var options = CreateValidOptions(commandLockSeconds: 29);

        Assert.False(options.TryValidate(out _, out var error));
        Assert.NotEmpty(error);
    }

    private static StreamGatewayOptions CreateValidOptions(
        string controlToken = "integration-control-token-at-least-32-bytes",
        int renewAfterSeconds = 40,
        int ticketRetentionHours = 24,
        string commandBaseUri = "https://gateway-control.integration.test/",
        string commandToken = "integration-command-token-at-least-32-bytes",
        int commandLockSeconds = 30,
        string publicBaseUri = "https://gateway.integration.test/",
        bool allowInsecureLoopbackHttpForDevelopment = false,
        string[]? trustedDevelopmentHttpHosts = null) =>
        new()
        {
            GatewayName = "integration",
            PublicBaseUri = publicBaseUri,
            ControlToken = controlToken,
            CommandBaseUri = commandBaseUri,
            CommandToken = commandToken,
            AllowInsecureLoopbackHttpForDevelopment = allowInsecureLoopbackHttpForDevelopment,
            TrustedDevelopmentHttpHosts = trustedDevelopmentHttpHosts ?? [],
            CommandLockSeconds = commandLockSeconds,
            TicketLifetimeSeconds = 20,
            LeaseLifetimeSeconds = 120,
            RenewAfterSeconds = renewAfterSeconds,
            MaxActiveSessionsPerClient = 64,
            MaxActiveSessionsPerUser = 128,
            MaxActiveSessionsPerCamera = 100,
            MaxMainProfileSessionsPerClient = 8,
            CleanupIntervalSeconds = 10,
            TicketRetentionHours = ticketRetentionHours
        };
}
