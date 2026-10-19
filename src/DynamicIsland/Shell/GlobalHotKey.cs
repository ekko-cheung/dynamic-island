using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DynamicIsland.Shell;

/// <summary>
/// A system-wide hotkey. Registered against the UI thread (no window needed): WM_HOTKEY arrives in the thread's
/// message queue and is picked up through <see cref="ComponentDispatcher"/>.
/// </summary>
public sealed class GlobalHotKey : IDisposable
{
    public const uint Alt = 0x0001;
    public const uint Ctrl = 0x0002;

    private const uint ModNoRepeat = 0x4000;
    private const int WmHotKey = 0x0312;
    private static int _nextId = 0xB000;

    private readonly int _id;
    private readonly Action _action;

    private GlobalHotKey(int id, Action action)
    {
        _id = id;
        _action = action;
        ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
    }

    /// <summary>Returns null when the combination is already taken by another program.</summary>
    public static GlobalHotKey? TryRegister(uint modifiers, char key, Action action)
    {
        var id = _nextId++;
        if (!RegisterHotKey(nint.Zero, id, modifiers | ModNoRepeat, char.ToUpperInvariant(key))) return null;
        return new GlobalHotKey(id, action);
    }

    private void OnMessage(ref MSG msg, ref bool handled)
    {
        if (msg.message != WmHotKey || (int)msg.wParam != _id) return;
        handled = true;
        _action();
    }

    public void Dispose()
    {
        ComponentDispatcher.ThreadPreprocessMessage -= OnMessage;
        UnregisterHotKey(nint.Zero, _id);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(nint hWnd, int id);
}
