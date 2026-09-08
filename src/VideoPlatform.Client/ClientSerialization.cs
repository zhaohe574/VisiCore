using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoPlatform.Client.Generated;

public partial class VideoPlatformClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings)
    {
        settings.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        settings.PropertyNameCaseInsensitive = true;
    }
}
