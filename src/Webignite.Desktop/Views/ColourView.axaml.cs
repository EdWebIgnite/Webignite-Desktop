using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Webignite.Desktop.ViewModels;

namespace Webignite.Desktop.Views;

/// <summary>Colours: type one or pick it from anywhere on the screen, with its shades and how it reads on white and black.</summary>
public partial class ColourView : UserControl
{
    private DispatcherTimer? picker;

    // The window the Enter and Esc handler went on while picking, so it comes off the same one.
    private TopLevel? keysOn;
    private string? colourBeforePicking;
    private DateTime pickingSince;

    public ColourView() => InitializeComponent();

    private ToolsViewModel? Tools => DataContext as ToolsViewModel;

    // Windows: the colour under the pointer is shown live until Enter keeps it or Esc puts the old one back.
    private void PickFromScreen(object? sender, RoutedEventArgs e)
    {
        if (!OperatingSystem.IsWindows() || Tools is not { } tools || picker is not null)
            return;
        colourBeforePicking = tools.ColourInput;
        pickingSince = DateTime.UtcNow;
        tools.Picking = true;
        keysOn = TopLevel.GetTopLevel(this);
        keysOn?.AddHandler(KeyDownEvent, PickingKeys, RoutingStrategies.Tunnel);
        picker = new DispatcherTimer(TimeSpan.FromMilliseconds(60), DispatcherPriority.Input, (_, _) => Sample());
        picker.Start();
    }

    private void Sample()
    {
        // Give up after a minute so the timer never runs forever.
        if (DateTime.UtcNow - pickingSince > TimeSpan.FromMinutes(1))
        {
            StopPicking(keep: true);
            return;
        }
        if (!OperatingSystem.IsWindows() || !GetCursorPos(out var point))
            return;
        var screen = GetDC(IntPtr.Zero);
        try
        {
            var colour = GetPixel(screen, point.X, point.Y);
            if (colour != uint.MaxValue)
                Tools?.ShowPicked((byte)(colour & 0xFF), (byte)(colour >> 8 & 0xFF), (byte)(colour >> 16 & 0xFF));
        }
        finally
        {
            if (ReleaseDC(IntPtr.Zero, screen) == 0)
                Webignite.Data.AppLog.Write("Picking a colour: the screen wasn't let go", new InvalidOperationException("ReleaseDC returned 0"));
        }
    }

    private void PickingKeys(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            StopPicking(keep: e.Key == Key.Enter);
            e.Handled = true;
        }
    }

    private void StopPicking(bool keep)
    {
        picker?.Stop();
        picker = null;
        keysOn?.RemoveHandler(KeyDownEvent, PickingKeys);
        keysOn = null;
        if (Tools is { } tools)
        {
            tools.Picking = false;
            if (!keep && colourBeforePicking is not null)
                tools.ColourInput = colourBeforePicking;
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        StopPicking(keep: true);
        base.OnDetachedFromVisualTree(e);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);
}
