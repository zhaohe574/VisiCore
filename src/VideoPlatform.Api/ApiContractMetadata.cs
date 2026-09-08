using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using VideoPlatform.Contracts;

namespace VideoPlatform.Api;

public static class ApiContractMetadata
{
    // 在 Build 前调用，集中为现有端点补齐 Produces 等价的 OpenAPI 元数据。
    public static IServiceCollection AddApiContractMetadata(this IServiceCollection services)
    {
        services.Configure<OpenApiOptions>("v2", options => options.AddOperationTransformer(TransformAsync));
        return services;
    }

    private static async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var id = operation.OperationId;
        if (id is null || context.Description.RelativePath?.StartsWith("api/v2/", StringComparison.Ordinal) != true) return;
        operation.Responses ??= new OpenApiResponses();
        if (ApiContractCatalog.Responses.TryGetValue(id, out var contract))
        {
            foreach (var status in operation.Responses.Keys.Where(key => key.StartsWith('2')).ToArray()) operation.Responses.Remove(status);
            var response = new OpenApiResponse { Description = contract.Status == 204 ? "操作完成，无响应正文" : "成功响应" };
            if (contract.BodyType is not null)
            {
                var schema = await ReferenceAsync(context, contract.BodyType, contract.SchemaName, ct);
                response.Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = schema } };
            }
            else if (contract.ContentTypes is not null)
                response.Content = contract.ContentTypes.ToDictionary(type => type, _ => new OpenApiMediaType { Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" } });
            operation.Responses[contract.Status.ToString()] = response;
        }
        if (id == "GetLatestRelease") operation.Responses["204"] = new OpenApiResponse { Description = "尚无匹配安装包；默认优先返回 MSI，同版本包列于 packages。" };
        if (ApiContractCatalog.Queries.TryGetValue(id, out var queries))
        {
            operation.Parameters ??= [];
            foreach (var query in queries)
            {
                var parameter = operation.Parameters.FirstOrDefault(p => p.In == ParameterLocation.Query && p.Name == query.Name);
                var schema = new OpenApiSchema
                {
                    Type = query.ValueType == typeof(int) ? JsonSchemaType.Integer : JsonSchemaType.String,
                    Format = query.ValueType == typeof(int) ? "int32" : query.ValueType == typeof(DateTimeOffset) ? "date-time" : null,
                    Default = query.Default is { } number ? JsonValue.Create(number) : null,
                    Minimum = query.Minimum?.ToString(), Maximum = query.Maximum?.ToString(),
                    Enum = query.Values?.Select(value => (JsonNode)JsonValue.Create(value)!).ToList()
                };
                if (parameter is null) operation.Parameters.Add(new OpenApiParameter { Name = query.Name, In = ParameterLocation.Query, Required = false, Schema = schema });
                else if (query.Values is not null && parameter is OpenApiParameter mutable) mutable.Schema = schema;
            }
        }
        if (id == "UploadRelease")
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["multipart/form-data"] = new() { Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object, Required = new HashSet<string>(["file", "version"]),
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                            ["version"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 32 },
                            ["releaseNotes"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 4096 },
                            ["minimumVersion"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 32 },
                            ["forceUpdate"] = new OpenApiSchema { Type = JsonSchemaType.Boolean, Default = JsonValue.Create(false) }
                        }
                    } }
                }
            };
        }
        var errorSchema = await ReferenceAsync(context, typeof(ErrorResponse), null, ct);
        foreach (var status in new[] { 400, 401, 403, 404, 409, 429, 500, 503 })
            operation.Responses.TryAdd(status.ToString(), new OpenApiResponse
            {
                Description = "请求失败；认证中间件也可能返回空正文。",
                Content = new Dictionary<string, OpenApiMediaType> { ["application/json"] = new() { Schema = errorSchema } }
            });
        var document = context.Document ?? throw new InvalidOperationException("OpenAPI 文档上下文不可用。");
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes.TryAdd("DesktopBearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", Description = "桌面登录令牌" });
        document.Components.SecuritySchemes.TryAdd("WebCookie", new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = "__Secure-VideoPlatform", Description = "浏览器 HttpOnly 安全 Cookie" });
        operation.Security = ApiContractCatalog.PublicOperations.Contains(id) ? [] :
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("DesktopBearer", document)] = [] },
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("WebCookie", document)] = [] }
        ];
        if (context.Description.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE")
        {
            operation.Parameters ??= [];
            if (!operation.Parameters.Any(p => p.In == ParameterLocation.Header && p.Name == "X-CSRF-Token"))
                operation.Parameters.Add(new OpenApiParameter { Name = "X-CSRF-Token", In = ParameterLocation.Header, Required = false,
                    Description = "使用浏览器 Cookie 写入时必须提供；桌面 Bearer 请求不需要。", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
        }
    }

    private static async Task<IOpenApiSchema> ReferenceAsync(OpenApiOperationTransformerContext context, Type type, string? alias, CancellationToken ct)
    {
        var document = context.Document ?? throw new InvalidOperationException("OpenAPI 文档上下文不可用。");
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        var name = alias ?? type.Name;
        if (document.Components.Schemas.ContainsKey(name)) return new OpenApiSchemaReference(name, document);
        var schema = await context.GetOrCreateSchemaAsync(type, cancellationToken: ct);
        document.Components.Schemas[name] = schema;
        if (schema is OpenApiSchema concrete)
        {
            if (type.IsArray && type.GetElementType() is { } element)
                concrete.Items = await ReferenceAsync(context, element, null, ct);
            foreach (var property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                var key = System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                if (concrete.Properties?.TryGetValue(key, out var propertySchema) != true) continue;
                var valueType = property.PropertyType;
                if (valueType.Namespace == typeof(ErrorResponse).Namespace && valueType.IsClass)
                    concrete.Properties[key] = await ReferenceAsync(context, valueType, null, ct);
                else if (valueType.IsGenericType && valueType.GetGenericArguments() is [var itemType]
                    && itemType.Namespace == typeof(ErrorResponse).Namespace && propertySchema is OpenApiSchema arraySchema)
                    arraySchema.Items = await ReferenceAsync(context, itemType, null, ct);
            }
        }
        return new OpenApiSchemaReference(name, document);
    }
}
