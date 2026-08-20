using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using DriftDeck.Controls;
using DriftDeck.Models;
using DriftDeck.Services;

namespace DriftDeck;

public sealed class PanelWindow : Window
{
    private const int WmSizing = 0x0214;
    /// <summary>Title strip plus the frame's top and bottom border.</summary>
    private const double ShadedHeight = 20;

    private OverlayWindowService? _windowService;
    private HwndSource? _source;
    private nint _handle;
    private bool _entryPlayed;
    private readonly double _unshadedMinHeight;

    public PanelDefinition Definition { get; }
    public PanelHost Host { get; }

    /// <summary>Rectangles this window should snap against while being resized.</summary>
    public Func<PanelWindow, IReadOnlyList<Rect>>? SnapRectsProvider { get; set; }

    /// <summary>Raised when the user clicks anywhere in this window, including web content.</summary>
    public event EventHandler? UserActivated;

    public bool IsShaded { get; private set; }

    public PanelWindow(PanelDefinition definition, PanelHost host)
    {
        Definition = definition;
        Host = host;
        Title = $"DriftDeck - {definition.Title}";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;

        // Resize from every edge and corner instead of one corner grip.
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });

        MinWidth = host.MinWidth;
        MinHeight = host.MinHeight;
        _unshadedMinHeight = host.MinHeight;
        Width = Math.Max(MinWidth, definition.Width);
        Height = Math.Max(MinHeight, definition.Height);
        Left = definition.X;
        Top = definition.Y;
        Opacity = 0;

        Content = host;
        host.Width = double.NaN;
        host.Height = double.NaN;
        host.HorizontalAlignment = HorizontalAlignment.Stretch;
        host.VerticalAlignment = VerticalAlignment.Stretch;

        SourceInitialized += OnSourceInitialized;
        // Clicking web content or the notes box activates the window; that is the real "raise me" signal.
        Activated += (_, _) => UserActivated?.Invoke(this, EventArgs.Empty);
        SizeChanged += (_, _) => Host.NotifyGeometryChanged();
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WindowProcedure);
        _windowService = new OverlayWindowService(_handle);
        ClampIntoView();

        if (Definition.IsCollapsed)
        {
            SetShaded(true, animate: false);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _source?.RemoveHook(WindowProcedure);
        _windowService?.Dispose();
    }

    // ============================ Ordering ============================

    /// <summary>Moves this panel to the front of the topmost band without taking focus.</summary>
    public void BringToFront() => WindowOrder.BringToFront(_handle);

    // ============================ Shade ============================

    /// <summary>
    /// Rolls the panel up to its title bar, or back down. A shaded panel keeps its position,
    /// so restoring it puts the content back exactly where the user left it — which is what an
    /// overlay wants, rather than a taskbar the overlay does not have.
    /// </summary>
    public void SetShaded(bool shaded, bool animate = true)
    {
        if (IsShaded == shaded)
        {
            return;
        }

        IsShaded = shaded;
        Definition.IsCollapsed = shaded;

        if (shaded)
        {
            // Only a live roll-up records the height to come back to. Restoring a shaded
            // panel from a saved layout must keep the height the layout already stored.
            if (animate)
            {
                Definition.RestoreHeight = Math.Max(_unshadedMinHeight, ActualHeight);
            }

            MinHeight = ShadedHeight;
            ResizeMode = ResizeMode.NoResize;
            AnimateHeight(ShadedHeight, animate);
        }
        else
        {
            MinHeight = _unshadedMinHeight;
            ResizeMode = ResizeMode.CanResize;
            AnimateHeight(Math.Max(_unshadedMinHeight, Definition.RestoreHeight), animate);
        }

        Host.SetShaded(shaded);
    }

    private void AnimateHeight(double target, bool animate)
    {
        if (!animate || !Motion.Enabled)
        {
            BeginAnimation(HeightProperty, null);
            Height = target;
            return;
        }

        Motion.To(this, HeightProperty, ActualHeight, target, Motion.Base);
    }

    // ============================ Geometry ============================

    /// <summary>Keeps a restored or freshly spawned panel on a monitor that actually exists.</summary>
    public void ClampIntoView()
    {
        var workArea = MonitorHelper.WorkAreaForPoint(new Point(Left + 40, Top + 14), this);
        if (workArea.IsEmpty)
        {
            return;
        }

        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height));
        Left = Math.Clamp(Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - Width));
        Top = Math.Clamp(Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - Height));
    }

    /// <summary>
    /// Applies the same snapping to window-chrome resizing that the title bar applies to
    /// dragging. Without this, edges dragged by the resize border ignore neighbours entirely.
    /// </summary>
    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != WmSizing || Snap.IsFreeMove || IsShaded)
        {
            return nint.Zero;
        }

        var target = PresentationSource.FromVisual(this)?.CompositionTarget;
        if (target is null)
        {
            return nint.Zero;
        }

        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var toDevice = target.TransformToDevice;

        // WM_SIZING speaks in physical pixels, so the guide lines are converted to match.
        var workArea = DeviceRect(MonitorHelper.WorkAreaForPoint(
            new Point(Left + ActualWidth / 2, Top + 14), this), toDevice);
        var others = new List<Rect>();
        foreach (var candidate in SnapRectsProvider?.Invoke(this) ?? [])
        {
            others.Add(DeviceRect(candidate, toDevice));
        }

        var vertical = Snap.VerticalLines(workArea, others);
        var horizontal = Snap.HorizontalLines(workArea, others);
        var minimum = toDevice.Transform(new Point(MinWidth, MinHeight));
        var edge = (int)wParam;

        if (edge is 1 or 4 or 7)
        {
            rect.Left = (int)Math.Round(Snap.Edge(rect.Left, vertical));
            rect.Left = Math.Min(rect.Left, rect.Right - (int)minimum.X);
        }
        else if (edge is 2 or 5 or 8)
        {
            rect.Right = (int)Math.Round(Snap.Edge(rect.Right, vertical));
            rect.Right = Math.Max(rect.Right, rect.Left + (int)minimum.X);
        }

        if (edge is 3 or 4 or 5)
        {
            rect.Top = (int)Math.Round(Snap.Edge(rect.Top, horizontal));
            rect.Top = Math.Min(rect.Top, rect.Bottom - (int)minimum.Y);
        }
        else if (edge is 6 or 7 or 8)
        {
            rect.Bottom = (int)Math.Round(Snap.Edge(rect.Bottom, horizontal));
            rect.Bottom = Math.Max(rect.Bottom, rect.Top + (int)minimum.Y);
        }

        Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return new nint(1);
    }

    private static Rect DeviceRect(Rect rect, Matrix toDevice)
    {
        var topLeft = toDevice.Transform(new Point(rect.Left, rect.Top));
        var bottomRight = toDevice.Transform(new Point(rect.Right, rect.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    // ============================ Visibility ============================

    /// <summary>Shows the window, fading in the first time so new panels do not pop.</summary>
    public void ShowPanel(double targetOpacity)
    {
        Show();
        if (_entryPlayed)
        {
            Opacity = targetOpacity;
            return;
        }

        _entryPlayed = true;
        // Motion.To stops the clock on completion, so later see-through changes are
        // not blocked by a lingering animation holding the property.
        Motion.To(this, OpacityProperty, 0, targetOpacity, Motion.Fast);
    }

    public void SetClickThrough(bool enabled) => _windowService?.SetClickThrough(enabled);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
