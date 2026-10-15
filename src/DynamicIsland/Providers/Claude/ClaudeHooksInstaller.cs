using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DynamicIsland.Providers.Claude;

public enum ClaudeHooksState { NotInstalled, Partial, Installed }

/// <summary>
/// Adds / removes the island's hooks in Claude Code's user settings (~/.claude/settings.json).
/// Only hook entries whose command posts to our <c>/claude</c> endpoint are ever touched; every other
/// setting and hook is left as it was. If the file can't be parsed strictly we refuse to write rather than guess.
/// </summary>
public static partial class ClaudeHooksInstaller
{
    private static readonly string[] Events = ["UserPromptSubmit", "PreToolUse", "PostToolUse", "Notification", "Stop", "SessionEnd"];

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // keep non-ASCII text readable, don't rewrite & < > etc.
    };

    [GeneratedRegex(@"127\.0\.0\.1:\d+/claude")]
    private static partial Regex OurCommand();

    public static string SettingsPath { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

    /// <exception cref="InvalidOperationException">The settings file exists but isn't something we can safely edit.</exception>
    public static ClaudeHooksState GetState()
    {
        var hooks = Load()?["hooks"] as JsonObject;
        var present = Events.Count(e => hooks?[e] is JsonArray list && list.Any(HasOurHook));
        return present == 0 ? ClaudeHooksState.NotInstalled
            : present == Events.Length ? ClaudeHooksState.Installed
            : ClaudeHooksState.Partial;
    }

    public static void Install(int port)
    {
        var root = Load() ?? [];
        var hooks = GetOrAddObject(root, "hooks");
        var command = $"curl.exe -s -m 2 -X POST http://127.0.0.1:{port}/claude -H \"Content-Type: application/json\" --data-binary @-";

        foreach (var name in Events)
        {
            var list = GetOrAddArray(hooks, name);
            if (IsAlreadyInstalled(list, command)) continue; // leave an identical entry exactly where it is

            RemoveOurHooks(list); // e.g. an older port
            list.Add(new JsonObject
            {
                ["hooks"] = new JsonArray(new JsonObject { ["type"] = "command", ["command"] = command }),
            });
        }
        Save(root);
    }

    public static void Uninstall()
    {
        var root = Load();
        if (root?["hooks"] is not JsonObject hooks) return;

        var changed = false;
        foreach (var name in Events)
        {
            if (hooks[name] is not JsonArray list) continue;
            if (!RemoveOurHooks(list)) continue;
            changed = true;
            if (list.Count == 0) hooks.Remove(name);
        }
        if (!changed) return;

        if (hooks.Count == 0) root.Remove("hooks");
        Save(root);
    }

    private static bool IsOurHook(JsonNode? hook) =>
        hook is JsonObject o && o["command"] is JsonValue v && v.TryGetValue<string>(out var command) && OurCommand().IsMatch(command);

    private static bool HasOurHook(JsonNode? entry) =>
        entry is JsonObject { } e && e["hooks"] is JsonArray inner && inner.Any(IsOurHook);

    private static bool IsAlreadyInstalled(JsonArray list, string command)
    {
        var ours = list.OfType<JsonObject>()
            .SelectMany(e => e["hooks"] is JsonArray inner ? inner : [])
            .Where(IsOurHook)
            .ToList();
        return ours.Count == 1 && ours[0]!["command"]!.GetValue<string>() == command;
    }

    /// <summary>Removes only our hook objects; an entry is dropped only if that leaves it with no hooks.</summary>
    private static bool RemoveOurHooks(JsonArray list)
    {
        var removed = false;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] is not JsonObject entry || entry["hooks"] is not JsonArray inner) continue;
            var removedHere = false;
            for (var j = inner.Count - 1; j >= 0; j--)
            {
                if (!IsOurHook(inner[j])) continue;
                inner.RemoveAt(j);
                removedHere = true;
            }
            removed |= removedHere;
            if (removedHere && inner.Count == 0 && !entry.Any(p => p.Key != "hooks" && p.Key != "matcher"))
                list.RemoveAt(i);
        }
        return removed;
    }

    private static JsonObject? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        var text = File.ReadAllText(SettingsPath);
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidOperationException("settings.json 的顶层不是对象,已放弃修改。");
        }
        catch (JsonException)
        {
            // Strict parse on purpose: comments or trailing commas would be lost on rewrite.
            throw new InvalidOperationException("settings.json 无法按标准 JSON 解析(可能含注释或多余的逗号),为避免破坏已有配置,已放弃修改。");
        }
    }

    private static JsonObject GetOrAddObject(JsonObject parent, string key)
    {
        if (parent[key] is null) parent[key] = new JsonObject();
        return parent[key] as JsonObject ?? throw new InvalidOperationException($"settings.json 里的 \"{key}\" 不是对象,已放弃修改。");
    }

    private static JsonArray GetOrAddArray(JsonObject parent, string key)
    {
        if (parent[key] is null) parent[key] = new JsonArray();
        return parent[key] as JsonArray ?? throw new InvalidOperationException($"settings.json 里的 hooks.{key} 不是数组,已放弃修改。");
    }

    private static void Save(JsonObject root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        if (File.Exists(SettingsPath)) File.Copy(SettingsPath, SettingsPath + ".bak", overwrite: true);

        // Write beside the target and swap, so a crash never leaves a half-written settings file.
        var temp = SettingsPath + ".island-tmp";
        File.WriteAllText(temp, root.ToJsonString(WriteOptions));
        File.Move(temp, SettingsPath, overwrite: true);
    }
}
