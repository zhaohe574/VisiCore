using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using VideoPlatform.Contracts;

var serializer = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
var schemas = new JsonObject();
var types = ApiContractCatalog.Responses.Values.Where(r => r.BodyType is not null)
    .Select(r => (Name: r.SchemaName ?? r.BodyType!.Name, Type: r.BodyType!)).Append((nameof(ErrorResponse), typeof(ErrorResponse))).Distinct();
foreach (var (name, type) in types)
{
    var schema = serializer.GetJsonSchemaAsNode(type, new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true });
    RewriteReferences(schema, name);
    schemas[name] = schema;
}
var catalog = new
{
    schemas,
    responses = ApiContractCatalog.Responses.ToDictionary(pair => pair.Key, pair => new
    {
        pair.Value.Status, schemaName = pair.Value.BodyType is null ? null : pair.Value.SchemaName ?? pair.Value.BodyType.Name, pair.Value.ContentTypes
    }),
    queries = ApiContractCatalog.Queries.ToDictionary(pair => pair.Key, pair => pair.Value.Select(query => new
    {
        query.Name, type = query.ValueType == typeof(int) ? "integer" : "string", format = query.ValueType == typeof(int) ? "int32" : query.ValueType == typeof(DateTimeOffset) ? "date-time" : null,
        query.Default, query.Minimum, query.Maximum, query.Values
    })),
    publicOperations = ApiContractCatalog.PublicOperations
};
Console.WriteLine(JsonSerializer.Serialize(catalog, new JsonSerializerOptions(serializer) { WriteIndented = true }));

static void RewriteReferences(JsonNode? node, string root)
{
    if (node is JsonObject value)
    {
        if (value["$ref"] is JsonValue reference && reference.TryGetValue<string>(out var path) && path.StartsWith('#'))
            value["$ref"] = $"#/components/schemas/{root}{path[1..]}";
        foreach (var property in value.ToArray()) RewriteReferences(property.Value, root);
    }
    else if (node is JsonArray array)
        foreach (var item in array) RewriteReferences(item, root);
}
