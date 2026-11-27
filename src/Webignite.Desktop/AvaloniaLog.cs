using Avalonia.Logging;
using Webignite.Data;

namespace Webignite.Desktop;

/// <summary>
/// Avalonia's own warnings and errors (a layout that can't settle, a part that fails to draw) go to the app's log as
/// well as wherever they went before, so a window that stops drawing leaves a trace to go on. Bindings are left
/// out, as a page shows a few harmless ones while it is built. At most a few hundred a run, so it can't flood.
/// </summary>
public sealed class AvaloniaLog(ILogSink? before) : ILogSink
{
    private int written;
    private const int Most = 300;

    public bool IsEnabled(LogEventLevel level, string area) =>
        before?.IsEnabled(level, area) == true || Wanted(level, area);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        before?.Log(level, area, source, messageTemplate);
        Write(level, area, source, messageTemplate, []);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        before?.Log(level, area, source, messageTemplate, propertyValues);
        Write(level, area, source, messageTemplate, propertyValues);
    }

    private static bool Wanted(LogEventLevel level, string area) => level >= LogEventLevel.Warning && area != LogArea.Binding;

    private void Write(LogEventLevel level, string area, object? source, string template, object?[] values)
    {
        if (!Wanted(level, area) || Interlocked.Increment(ref written) > Most)
            return;
        var text = template;
        foreach (var value in values)
        {
            var open = text.IndexOf('{', StringComparison.Ordinal);
            var close = open < 0 ? -1 : text.IndexOf('}', open);
            if (close < 0)
                break;
            text = string.Concat(text.AsSpan(0, open), value?.ToString() ?? "null", text.AsSpan(close + 1));
        }
        AppLog.Write($"Avalonia {level} ({area}, {source?.GetType().Name ?? "-"}): {text}", values.OfType<Exception>().FirstOrDefault());
    }
}
