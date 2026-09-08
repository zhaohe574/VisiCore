# VisiCore（视枢）第二版 C# 客户端

该项目默认编译随源码提交的 `Generated/VideoPlatformClient.g.cs`，由 NSwag 根据实际 OpenAPI 生成，干净克隆不依赖本机 `artifacts`。所有路径、请求、响应及接口声明均来自生成产物；其他文件负责令牌注入、JSON 数字字符串兼容和最新版本 204 可空结果封装。

重新生成默认仍写入 `artifacts/v2/generated/csharp`，可使用 `-p:GeneratedClientDirectory=<生成文件目录>` 单独编译验证。验证并审查差异后同步入库文件，不手动修改生成的接口和 DTO。

完整生成、Web／桌面接入与验证方式见 `tools/VideoPlatform.ClientGenerator/README.md`。

生成后执行：

```powershell
& 'C:/Users/57477/AppData/Local/VideoPlatform/dotnet/dotnet.exe' build src/VideoPlatform.Client/VideoPlatform.Client.csproj
```

桌面引用本项目后，使用 `VideoPlatform.Client.Generated.IVideoPlatformClient` 或 `VideoPlatformClient`。`HttpClient.BaseAddress` 为站点根地址；HttpClient、令牌安全存储、登录刷新和媒体续期由调用方管理。
