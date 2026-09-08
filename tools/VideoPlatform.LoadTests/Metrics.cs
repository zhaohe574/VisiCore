using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VideoPlatform.Infrastructure;

namespace VideoPlatform.LoadTests;

public sealed record RequestSample(string Stage, string Operation, int Status, bool Success, double Milliseconds, string? ErrorCode);
public sealed record MetricSummary(string Stage, string Operation, int Requests, int Failures, double P50Ms, double P95Ms, double P99Ms, double MaxMs, Dictionary<int, int> StatusCounts);

public sealed class Metrics
{
    private readonly ConcurrentQueue<RequestSample> _samples = new();
    public RequestSample[] Samples => _samples.ToArray();

    public async Task<JsonNode?> SendAsync(HttpClient client, string stage, string operation, HttpMethod method, string path,
        object? body = null, string? token = null, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonDefaults.Options);
        var watch = Stopwatch.StartNew();
        var recorded = false;
        try
        {
            using var response = await client.SendAsync(request, ct);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            watch.Stop();
            var json = bytes.Length == 0 ? null : JsonNode.Parse(bytes);
            var ok = response.IsSuccessStatusCode;
            _samples.Enqueue(new(stage, operation, (int)response.StatusCode, ok, watch.Elapsed.TotalMilliseconds, ok ? null : json.Text("code", "http.failed")));
            recorded = true;
            if (!ok) throw new InvalidOperationException($"容量请求 {operation} 返回 HTTP {(int)response.StatusCode}，错误码 {json.Text("code")}。");
            return json;
        }
        catch (Exception ex)
        {
            if (!recorded) _samples.Enqueue(new(stage, operation, 0, false, watch.Elapsed.TotalMilliseconds, ex.GetType().Name));
            throw;
        }
    }

    public MetricSummary[] Summaries()
        => Samples.GroupBy(s => (s.Stage, s.Operation)).Select(g =>
        {
            var values = g.Select(s => s.Milliseconds).Order().ToArray();
            return new MetricSummary(g.Key.Stage, g.Key.Operation, values.Length, g.Count(s => !s.Success),
                Percentile(values, .50), Percentile(values, .95), Percentile(values, .99), values[^1], g.GroupBy(s => s.Status).ToDictionary(s => s.Key, s => s.Count()));
        }).OrderBy(s => s.Stage).ThenBy(s => s.Operation).ToArray();

    public static double Percentile(double[] sorted, double percentile)
        => sorted.Length == 0 ? 0 : Math.Round(sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1)], 2);
}
