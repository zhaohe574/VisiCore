# PostgreSQL 集成测试

默认使用 Testcontainers 启动 PostgreSQL 16。没有 Docker 时，可显式设置 `VIDEO_PLATFORM_TEST_POSTGRES_CONNECTION` 运行本机临时数据库测试。

连接必须使用回环地址，且数据库名必须以 `video_platform_integration_` 开头。例如：

```powershell
$env:VIDEO_PLATFORM_TEST_POSTGRES_CONNECTION = 'Host=127.0.0.1;Port=55432;Database=video_platform_integration_local;Username=postgres;Pooling=false'
dotnet test .\tests\VideoPlatform.Api.IntegrationTests\VideoPlatform.Api.IntegrationTests.csproj --configuration Debug
```

连接中的数据库名仅作为安全标识。每个 Fixture 会基于该连接生成独立的随机临时数据库，测试开始前创建、结束后删除，因此可以并行执行。禁止指向开发库、生产库或非回环数据库。
