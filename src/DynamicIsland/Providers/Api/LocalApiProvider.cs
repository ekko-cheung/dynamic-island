using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using DynamicIsland.Core;
using DynamicIsland.Providers.Claude;
using DynamicIsland.Providers.Notifications;
using DynamicIsland.Providers.Timers;
using DynamicIsland.Settings;

namespace DynamicIsland.Providers.Api;

/// <summary>
/// Local HTTP endpoint so any program or script can show a notification on the island:
/// <c>POST http://127.0.0.1:{port}/notify</c> with a JSON body.
/// Loopback only, and JSON-only so a web page cannot trigger it without a CORS preflight (which we never grant).
/// </summary>
public sealed class LocalApiProvider(AppSettings settings, ClaudeActivityProvider claude, TimerReminderProvider timers) : IActivityProvider
{
    private const int MaxBodyBytes = 64 * 1024;
    // Claude Code hook payloads can embed whole tool inputs/outputs.
    private const int MaxHookBodyBytes = 8 * 1024 * 1024;
    private const int MaxTitleLength = 200;
    private const int MaxBodyLength = 1000;
    private static readonly TimeSpan DefaultShowFor = TimeSpan.FromSeconds(6);

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed record NotifyRequest(string? Title, string? Body, string? App, int? DurationMs, bool? Sticky);
    private sealed record TimerRequest(double? Minutes, double? Seconds, string? Label);
    private sealed record ReminderRequest(string? Text, string? At, double? InMinutes, bool? Daily);

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
            var path = request.Url?.AbsolutePath;
            if (path is not ("/notify" or "/claude" or "/timer" or "/reminder")) { await ReplyAsync(context, 404, "not found"); return; }
            if (request.HttpMethod != "POST") { await ReplyAsync(context, 405, "use POST"); return; }
            if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
            { await ReplyAsync(context, 415, "Content-Type must be application/json"); return; }

            var isHook = path == "/claude";
            var body = await ReadBodyAsync(request, isHook ? MaxHookBodyBytes : MaxBodyBytes);
            if (body is null) { await ReplyAsync(context, 413, "body too large"); return; }

            if (isHook)
            {
                ClaudeHookEvent? hook;
                try { hook = JsonSerializer.Deserialize<ClaudeHookEvent>(body, Json); }
                catch (JsonException) { await ReplyAsync(context, 400, "invalid JSON"); return; }
                if (hook is null) { await ReplyAsync(context, 400, "empty body"); return; }

                await Application.Current.Dispatcher.InvokeAsync(() => claude.Report(hook));
                await ReplyAsync(context, 200, null);
                return;
            }

            if (path == "/timer")
            {
                if (!TryParse<TimerRequest>(body, out var timer)) { await ReplyAsync(context, 400, "invalid JSON"); return; }
                var seconds = timer.Seconds ?? timer.Minutes * 60;
                if (seconds is not > 0 or > 24 * 3600) { await ReplyAsync(context, 400, "minutes or seconds must be > 0 and at most 24 hours"); return; }

                var label = Truncate(timer.Label, 60);
                await Application.Current.Dispatcher.InvokeAsync(() => timers.StartCountdown(TimeSpan.FromSeconds(seconds.Value), label));
                await ReplyAsync(context, 200, null);
                return;
            }

            if (path == "/reminder")
            {
                if (!TryParse<ReminderRequest>(body, out var reminder)) { await ReplyAsync(context, 400, "invalid JSON"); return; }
                var reminderText = Truncate(reminder.Text, MaxTitleLength);
                if (reminderText.Length == 0) { await ReplyAsync(context, 400, "text is required"); return; }
                if (!TryResolveDue(reminder, out var due, out var problem)) { await ReplyAsync(context, 400, problem); return; }

                await Application.Current.Dispatcher.InvokeAsync(() => timers.AddReminder(reminderText, due, reminder.Daily == true));
                await ReplyAsync(context, 200, null);
                return;
            }

            NotifyRequest? payload;
            try { payload = JsonSerializer.Deserialize<NotifyRequest>(body, Json); }
            catch (JsonException) { await ReplyAsync(context, 400, "invalid JSON"); return; }

            var title = Truncate(payload?.Title, MaxTitleLength);
            var text = Truncate(payload?.Body, MaxBodyLength);
            if (title.Length == 0 && text.Length == 0) { await ReplyAsync(context, 400, "title or body is required"); return; }

            var app = Truncate(payload?.App, 60);
            var showFor = payload?.DurationMs is { } ms ? TimeSpan.FromMilliseconds(Math.Clamp(ms, 1000, 30000)) : DefaultShowFor;

            await Application.Current.Dispatcher.InvokeAsync(() => Show(app.Length > 0 ? app : "API", title, text, showFor, payload?.Sticky == true));
            await ReplyAsync(context, 200, null);
        }
        catch
        {
            try { await ReplyAsync(context, 500, "internal error"); } catch { /* client went away */ }
        }
    }

    // UI thread: the hub and DispatcherTimer are not thread-safe.
    private void Show(string app, string title, string body, TimeSpan showFor, bool sticky)
    {
        var id = $"api-{Guid.NewGuid():N}";
        Action? dismiss = sticky ? () => ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, null)) : null;
        ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, new NotificationActivity(id, app, title, body, icon: null, dismiss)));
        if (sticky) return; // stays until the user closes it

        var timer = new DispatcherTimer { Interval = showFor };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(id, null));
        };
        timer.Start();
    }

    private static bool TryParse<T>(string body, out T value) where T : class
    {
        try { value = JsonSerializer.Deserialize<T>(body, Json)!; }
        catch (JsonException) { value = null!; }
        return value is not null;
    }

    /// <summary>"at" is a time of day ("18:30", next occurrence) or a full date-time; or "inMinutes" from now.</summary>
    private static bool TryResolveDue(ReminderRequest request, out DateTime due, out string problem)
    {
        var now = DateTime.Now;
        due = default;
        problem = "";

        if (request.InMinutes is { } minutes)
        {
            if (minutes is > 0 and <= 366 * 24 * 60) { due = now.AddMinutes(minutes); return true; }
            problem = "inMinutes must be > 0 and at most one year";
            return false;
        }

        var at = request.At?.Trim() ?? "";
        if (TimeSpan.TryParseExact(at, @"h\:mm", CultureInfo.InvariantCulture, out var timeOfDay) && timeOfDay < TimeSpan.FromDays(1))
        {
            due = now.Date + timeOfDay;
            if (due <= now) due = due.AddDays(1);
            return true;
        }
        if (DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var moment))
        {
            due = moment.LocalDateTime;
            if (due > now) return true;
            problem = "at is in the past";
            return false;
        }

        problem = "give either at (\"18:30\" or an ISO date-time) or inMinutes";
        return false;
    }

    private static async Task<string?> ReadBodyAsync(HttpListenerRequest request, int maxBytes)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await request.InputStream.ReadAsync(chunk)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) return null;
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
