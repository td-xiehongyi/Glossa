using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;

internal static class PopupGlass
{
    // The accent policy is optional. Older compositors keep the original WPF tint.
    [StructLayout(LayoutKind.Sequential)]
    struct AccentPolicy { public int State, Flags, Color, Animation; }
    [StructLayout(LayoutKind.Sequential)]
    struct AttributeData { public int Attribute; public IntPtr Data; public UIntPtr Size; }

    [DllImport("user32.dll")]
    static extern bool SetWindowCompositionAttribute(IntPtr window, ref AttributeData data);
    internal static Action Attach(Window card, Border surface, Func<bool> enabled = null)
    {
        if (SystemParameters.HighContrast) return null;
        var backdrop = new Window {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true, Background = Brushes.Transparent,
            ShowActivated = false, ShowInTaskbar = false, Topmost = card.Topmost,
            Width = 1, Height = 1
        };
        if (!Enable(backdrop)) { backdrop.Close(); return null; }
        card.Owner = backdrop;
        // A separate surface keeps the native rectangular blur inside the rounded face.
        Action align = delegate {
            if (!card.IsVisible || (enabled != null && !enabled())) { backdrop.Hide(); return; }
            if (!surface.IsArrangeValid || surface.ActualHeight <= 8) return;
            Point origin = surface.TranslatePoint(new Point(), card);
            double inset = Math.Ceiling(surface.CornerRadius.TopLeft * (1 - Math.Sqrt(0.5)));
            backdrop.Left = card.Left + origin.X + inset;
            backdrop.Top = card.Top + origin.Y + inset;
            backdrop.Width = Math.Max(1,surface.ActualWidth - 2 * inset);
            backdrop.Height = Math.Max(1,surface.ActualHeight - 2 * inset);
            if (!backdrop.IsVisible) backdrop.Show();
        };
        card.LocationChanged += delegate { align(); };
        card.LayoutUpdated += delegate { align(); };
        card.IsVisibleChanged += delegate { if (card.IsVisible) align(); else backdrop.Hide(); };
        card.Closed += delegate { backdrop.Close(); };
        return align;
    }
    static bool Enable(Window window)
    {
        int size = Marshal.SizeOf(typeof(AccentPolicy));
        IntPtr buffer = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(new AccentPolicy { State = 3 }, buffer, false);
            var data = new AttributeData { Attribute = 19, Data = buffer, Size = (UIntPtr)size };
            return SetWindowCompositionAttribute(new WindowInteropHelper(window).EnsureHandle(), ref data);
        }
        catch (EntryPointNotFoundException) { return false; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
