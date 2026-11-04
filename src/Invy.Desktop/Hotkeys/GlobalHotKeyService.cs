using System.Runtime.Versioning;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Invy.Desktop.Hotkeys;

/// <summary>
/// Глобальные горячие клавиши Windows через <c>RegisterHotKey</c>.
/// Регистрация без окна: WM_HOTKEY приходит в очередь сообщений отдельного потока.
/// Событие <see cref="Pressed"/> вызывается в этом потоке — подписчик сам переходит в UI-поток.
/// </summary>
[SupportedOSPlatform("windows10.0")]
public sealed class GlobalHotKeyService : IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Dictionary<int, HotKeyGesture> _requested;
    private readonly Dictionary<int, bool> _registered = [];
    private uint _threadId;

    public GlobalHotKeyService(IReadOnlyDictionary<int, HotKeyGesture> hotKeys)
    {
        _requested = new Dictionary<int, HotKeyGesture>(hotKeys);
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "Invy global hotkeys" };
        _thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>Нажато сочетание; аргумент — id из словаря, переданного в конструктор.</summary>
    public event EventHandler<int>? Pressed;

    /// <summary>Удалось ли зарегистрировать сочетание (false — занято другой программой).</summary>
    public bool IsRegistered(int id) => _registered.TryGetValue(id, out var ok) && ok;

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
            _thread.Join(TimeSpan.FromSeconds(2));
        }

        _ready.Dispose();
    }

    private void MessageLoop()
    {
        _threadId = PInvoke.GetCurrentThreadId();

        // Создаём очередь сообщений потока до того, как кто-то пошлёт в неё WM_QUIT.
        PInvoke.PeekMessage(out _, HWND.Null, PInvoke.WM_USER, PInvoke.WM_USER, PEEK_MESSAGE_REMOVE_TYPE.PM_NOREMOVE);

        foreach (var (id, gesture) in _requested)
        {
            var modifiers = (HOT_KEY_MODIFIERS)(uint)gesture.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
            _registered[id] = PInvoke.RegisterHotKey(HWND.Null, id, modifiers, gesture.VirtualKey);
        }

        _ready.Set();

        while (PInvoke.GetMessage(out var message, HWND.Null, 0, 0).Value > 0)
        {
            if (message.message == PInvoke.WM_HOTKEY)
            {
                Pressed?.Invoke(this, (int)message.wParam.Value);
            }
        }

        foreach (var (id, ok) in _registered)
        {
            if (ok)
            {
                PInvoke.UnregisterHotKey(HWND.Null, id);
            }
        }
    }
}
