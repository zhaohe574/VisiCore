using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VisiCore.Persistence;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .Build();
var connectionString = configuration.GetConnectionString("Platform");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("缺少 ConnectionStrings__Platform，无法初始化数据库。");
    return 2;
}

var services = new ServiceCollection();
services.AddDbContext<PlatformDbContext>(options => options.UseNpgsql(connectionString));
services.AddSingleton<Argon2PasswordHasher>();
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();

await dbContext.Database.MigrateAsync();
if (await dbContext.Users.AnyAsync())
{
    Console.Error.WriteLine("数据库已经包含账号，初始化已拒绝，现有数据未被修改。");
    return 3;
}

var username = configuration["Setup:Username"]?.Trim();
var password = configuration["Setup:Password"];
if (string.IsNullOrWhiteSpace(username))
{
    Console.Write("初始管理员账号 [admin]：");
    username = Console.ReadLine()?.Trim();
    if (string.IsNullOrWhiteSpace(username))
    {
        username = "admin";
    }
}
if (string.IsNullOrWhiteSpace(password))
{
    if (Console.IsInputRedirected)
    {
        Console.Error.WriteLine("非交互初始化必须通过 Setup__Password 提供密码。");
        return 2;
    }
    Console.Write("初始管理员密码（输入不回显）：");
    password = ReadPassword();
    Console.WriteLine();
}

if (!Regex.IsMatch(username, "^[A-Za-z0-9._-]{3,64}$") || password.Length < 12)
{
    Console.Error.WriteLine("账号必须为 3 至 64 位字母、数字、点、下划线或连字符；密码至少 12 位。");
    return 2;
}

var hasher = scope.ServiceProvider.GetRequiredService<Argon2PasswordHasher>();
dbContext.Users.Add(new UserEntity
{
    Id = Guid.NewGuid(),
    Username = username,
    PasswordHash = hasher.Hash(password),
    IsSystemAdministrator = true,
    CreatedAt = DateTimeOffset.UtcNow
});
await dbContext.SaveChangesAsync();
Console.WriteLine($"已创建系统管理员 {username}。请启动常驻服务后登录管理端。");
return 0;

static string ReadPassword()
{
    var characters = new List<char>();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            return new string(characters.ToArray());
        }
        if (key.Key == ConsoleKey.Backspace)
        {
            if (characters.Count > 0)
            {
                characters.RemoveAt(characters.Count - 1);
            }
            continue;
        }
        if (!char.IsControl(key.KeyChar))
        {
            characters.Add(key.KeyChar);
        }
    }
}
