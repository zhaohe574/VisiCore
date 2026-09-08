using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

internal static class AdapterEndpoints
{
    public static void Map(WebApplication app, DeviceRegistry registry, ExportService exports)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "hikvision-adapter", version = "2.0.0", detail = registry.Health }));
        app.MapPut("/internal/devices/{deviceId:long}", async (long deviceId, DeviceRegistration request) => Results.Ok(await registry.RegisterAsync(deviceId, request)));
        app.MapDelete("/internal/devices/{deviceId:long}", async (long deviceId) => { await registry.DeleteAsync(deviceId); return Results.NoContent(); });
        app.MapPost("/internal/devices/{deviceId:long}/sync", async (long deviceId) => Results.Ok(await registry.SyncAsync(deviceId)));
        app.MapPost("/internal/devices/{deviceId:long}/live", async (long deviceId, LiveStartRequest request, HttpContext context) =>
            Results.Ok(await registry.UseAsync(deviceId, entry => (entry.Live ?? throw new AdapterException(409, "DEVICE_DISABLED", "设备已禁用。")).StartAsync(request, context.RequestAborted))));
        app.MapDelete("/internal/devices/{deviceId:long}/live/{sessionId:guid}", async (long deviceId, Guid sessionId) =>
        {
            await registry.UseAsync(deviceId, async entry => { if (entry.Live is not null) await entry.Live.StopAsync(sessionId); return true; });
            return Results.NoContent();
        });
        app.MapPost("/internal/devices/{deviceId:long}/recordings/search", async (long deviceId, RecordingSearchRequest request) =>
        {
            Validate.Range(request.Channel, request.Start, request.End);
            return Results.Ok(await registry.UseAsync(deviceId, entry => Task.FromResult(entry.Required.SearchRecordings(request.Channel, request.Start, request.End))));
        });
        app.MapPost("/internal/devices/{deviceId:long}/playback", async (long deviceId, PlaybackStartRequest request) => Results.Ok(await registry.StartPlaybackAsync(deviceId, request)));
        app.MapGet("/internal/devices/{deviceId:long}/playback/{sessionId:guid}", async (long deviceId, Guid sessionId) => Results.Ok(await registry.UseAsync(deviceId, entry => Task.FromResult(DeviceRegistry.Playback(entry, sessionId).Summary()))));
        app.MapDelete("/internal/devices/{deviceId:long}/playback/{sessionId:guid}", async (long deviceId, Guid sessionId) => { await registry.StopPlaybackAsync(deviceId, sessionId); return Results.NoContent(); });
        app.MapPost("/internal/devices/{deviceId:long}/playback/{sessionId:guid}/control", async (long deviceId, Guid sessionId, PlaybackControlRequest request) =>
            Results.Ok(await registry.UseAsync(deviceId, entry => DeviceRegistry.Playback(entry, sessionId).ControlAsync(request))));
        app.MapPost("/internal/devices/{deviceId:long}/ptz", async (long deviceId, PtzRequest request) =>
        {
            await registry.UseAsync(deviceId, entry => { entry.Required.Ptz(request); return Task.FromResult(true); }); return Results.NoContent();
        });
        app.MapPost("/internal/devices/{deviceId:long}/ptz/preset", async (long deviceId, PresetRequest request) =>
        {
            await registry.UseAsync(deviceId, entry => { entry.Required.Preset(request); return Task.FromResult(true); }); return Results.NoContent();
        });
        app.MapPost("/internal/devices/{deviceId:long}/exports", async (long deviceId, ExportRequest request) => Results.Ok(await registry.UseAsync(deviceId, entry => Task.FromResult(exports.Start(entry.Required, request)))));
        app.MapGet("/internal/devices/{deviceId:long}/exports/{jobId:guid}", (long deviceId, Guid jobId) => Results.Ok(exports.Get(deviceId, jobId)));
        app.MapDelete("/internal/devices/{deviceId:long}/exports/{jobId:guid}", async (long deviceId, Guid jobId) => Results.Ok(await exports.CancelAsync(deviceId, jobId)));
        app.MapGet("/internal/sessions", async () => Results.Ok(await registry.SessionsAsync()));
        app.MapGet("/internal/devices/{deviceId:long}/events", (long deviceId, string? after, int? limit) => Results.Ok(registry.Find(deviceId).Journal.Read(after, limit ?? 100)));
        app.MapPost("/internal/devices/{deviceId:long}/events/ack", (long deviceId, AckRequest request) => { registry.Find(deviceId).Journal.Ack(request.Cursor); return Results.NoContent(); });
    }
}
