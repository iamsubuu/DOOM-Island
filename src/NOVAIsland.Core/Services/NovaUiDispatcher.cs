namespace NOVAIsland.Core.Services;

/// <summary>
/// Marshals background service updates (SMTC, SQLite watchers, clipboard, activity, notifications)
/// cleanly onto the WinUI UI thread so bound ObservableCollections and properties never mutate off-thread.
/// Defaults to inline execution in headless unit tests.
/// </summary>
public static class NovaUiDispatcher
{
    private static Action<Action>? _dispatcher;
    private static readonly AsyncLocal<Action<Action>?> _scopedOverride = new();

    public static void Initialize(Action<Action> dispatcher) => _dispatcher = dispatcher;

    public static void Reset() => _dispatcher = null;

    public static IDisposable ScopedOverrideForTests(Action<Action> dispatcher)
    {
        var previous = _scopedOverride.Value;
        _scopedOverride.Value = dispatcher;
        return new ScopeReset(previous);
    }

    public static void Post(Action action)
    {
        var d = _scopedOverride.Value ?? _dispatcher;
        if (d is not null)
        {
            d(action);
        }
        else
        {
            action();
        }
    }

    private sealed class ScopeReset(Action<Action>? previous) : IDisposable
    {
        public void Dispose() => _scopedOverride.Value = previous;
    }
}
