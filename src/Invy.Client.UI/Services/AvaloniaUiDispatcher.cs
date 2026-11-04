using Avalonia.Threading;
using Invy.Client.Core.Connection;

namespace Invy.Client.UI.Services;

/// <summary>События сервера (SignalR) — в поток UI Avalonia.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public static AvaloniaUiDispatcher Instance { get; } = new();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
