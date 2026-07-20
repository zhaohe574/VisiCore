using VisiCore.Persistence;
using Xunit;

namespace VisiCore.Api.Tests;

public sealed class PlatformUsernamePolicyTests
{
    [Theory]
    [InlineData("Admin@Example.COM", "admin@example.com")]
    [InlineData("123_admin", "123_admin")]
    [InlineData("MixedCase", "MixedCase")]
    public void 邮箱规范化而普通账号保留大小写(string input, string expected)
    {
        var valid = PlatformUsernamePolicy.TryNormalize(input, out var username, out _);

        Assert.True(valid);
        Assert.Equal(expected, username);
    }

    [Theory]
    [InlineData("admin name")]
    [InlineData("admin@localhost")]
    [InlineData("admin!name")]
    [InlineData("admin\tname")]
    public void 非法账号格式被拒绝(string input)
    {
        Assert.False(PlatformUsernamePolicy.TryNormalize(input, out _, out _));
    }
}
