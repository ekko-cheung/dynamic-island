using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Settings;

namespace DynamicIsland.Providers.Api;

/// <summary>
/// Local HTTP endpoint so any program or script can show a notification on the island:
/// <c>POST http://127.0.0.1:{port}/notify</c> with a JSON body.
/// Loopback only, and JSON-only so a web page cannot trigger it without a CORS preflight (which we never grant).
/// </summary>
public sealed class LocalApiProvider(AppSettings settings) : IActivityProvider
{
    private const int MaxBodyBytes = 64 * 1024;
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 1000;
    private static readonly TimeSpan DefaultShowFor = TimeSpan.FromSeconds(6);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed record NotifyRequest(string? Title, string? Body, string? App, int? DurationMs);

    public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;

    public Task StartAsync()
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{settings.ApiPort}/");
        listener.Start(); // throws if the port is taken; the caller reports it
        _ = Task.Run(() => AcceptLoopAsync(listener));
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch { break; } // listener closed
            _ = HandleAsync(context);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            if (request.Url?.AbsolutePath != "/notify") { await ReplyAsync(context, 404, "not found"); return; }
            if (request.HttpMethod != "POST") { await ReplyAsync(context, 405, "use POST"); return; }
            if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
            { await ReplyAsync(context, 415, "Content-Type must be application/json"); return; }

            var body = await ReadBodyAsync(request);
            if (body is null) { await ReplyAsync(context, 413, "body too large"); return; }

            NotifyRequest? payload;
            try { payload = JsonSerializer.Deserialize<NotifyRequest>(body, Json); }
            catch (JsonException) { await ReplyAsync(context, 400, "invalid JSON"); return; }

            var title = Truncate(payload?.Title, MaxTitleLength);
            var text = Truncate(payload?.Body, MaxBodyLength);
            if (title.Length == 0 && text.Length == 0) { await ReplyAsync(context, 400, "title or body is required"); return; }

            var app = Truncate(payload?.App, 60);
            var showFor = payload?.DurationMs is { } ms ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 1000, 30000)) : DefaultShowFor;

            await Application.Current.Dispatcher.InvokeAsync(() => Show(app.Length > 0 ? app : "API", title, text, showFor));
            await ReplyAsync(context, 200, null);
        }
        catch
        {
            try { await ReplyAsync(context, 500, "internal error"); } catch { /* client went away */ }
        }
    }

    // UI thread: the hub and DispatcherTimer are not thread-safe.
    private void Show(string app, string title, string body, TimeSpan showFor)
    {
        var id = $"api-{Guid.NewGuid():N}";
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, new NotificationActivity(id, app, title, body, icon: null)));

        var timer = new DispatcherTimer { Interval = showFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, null));
        };
        timer.Start();
    }

    private static async Task<string?> ReadBodyAsync(HttpListenerRequest request)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes) return null;
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string Truncate(string? value, int max)
    {
        value = value?.Trim() ?? "";
        return value.Length <= max ? value : value[..max];
    }

    private static async Task ReplyAsync(HttpListenerContext context, int status, string? error)
    {
        var json = error is null ? "{\"ok\":true}" : JsonSerializer.Serialize(new { ok = false, error });
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
    }
}
