namespace ComiCraft.Viewer;

/// <summary>How a page is fitted to the window.</summary>
internal enum Fit
{
    /// <summary>The whole page is seen at once.</summary>
    Page,

    /// <summary>The page fills the window's width, and is scrolled when it is taller than the window.</summary>
    Width,

    /// <summary>The page is as large as the reader zoomed it, and is scrolled across and down.</summary>
    Zoom,
}

/// <summary>A page's size and the size of the area it is shown in, in the same units (pixels).</summary>
internal readonly record struct Frame(float PageWidth, float PageHeight, float AreaWidth, float AreaHeight);

/// <summary>Where the page is drawn in the area; it can start above the area, or to its left, when scrolled.</summary>
internal readonly record struct Placement(float X, float Y, float Width, float Height);

/// <summary>
/// How a page is seen: how it is fitted or zoomed, and how far across and down it the reader has
/// scrolled. The place is kept as a share of the page's width and height, and the zoom as a share
/// of the whole page's size, so both hold when the window changes size or the page is turned, and
/// "the bottom" means the bottom of whichever page is shown next.
/// </summary>
internal sealed class PageView
{
    // half a pixel: closer than that to the end of the page is at the end of it
    private const float Near = 0.5f;

    /// <summary>The most a page is zoomed: each of its pixels four pixels across, which is more than any lettering needs.</summary>
    public const float MostPixels = 4;

    public Fit Fit { get; set; } = Fit.Page;

    /// <summary>How large the page is, zoomed, as a multiple of the whole page fitted to the window: 1 is the whole page.</summary>
    public double Zoom { get; private set; } = 1;

    /// <summary>How far down the page the top of the area is, from 0 (its top) to 1 (past its end, kept at its bottom).</summary>
    public double Top { get; private set; }

    /// <summary>How far across the page the left of the area is, from 0 (its left) to 1 (past its right, kept at its right).</summary>
    public double Left { get; private set; }

    public Placement Place(Frame frame)
    {
        float scale = Scale(frame);
        float width = frame.PageWidth * scale, height = frame.PageHeight * scale;
        float x = width <= frame.AreaWidth ? (frame.AreaWidth - width) / 2 : -ScrolledAcross(frame);
        float y = height <= frame.AreaHeight ? (frame.AreaHeight - height) / 2 : -Scrolled(frame);
        return new Placement(x, y, width, height);
    }

    /// <summary>How much of the page is below the area when it is scrolled to the top; nothing when it fits.</summary>
    public float Overflow(Frame frame) => Math.Max(0, frame.PageHeight * Scale(frame) - frame.AreaHeight);

    /// <summary>How much of the page is to the right of the area when it is scrolled to the left; nothing when it fits.</summary>
    public float OverflowAcross(Frame frame) => Math.Max(0, frame.PageWidth * Scale(frame) - frame.AreaWidth);

    public bool AtTop(Frame frame) => Scrolled(frame) <= Near;

    public bool AtBottom(Frame frame) => Scrolled(frame) >= Overflow(frame) - Near;

    public bool AtLeft(Frame frame) => ScrolledAcross(frame) <= Near;

    public bool AtRight(Frame frame) => ScrolledAcross(frame) >= OverflowAcross(frame) - Near;

    /// <summary>Scrolls down by the distance (up, when it is negative), no further than the page goes; returns whether it moved.</summary>
    public bool Scroll(Frame frame, float distance)
    {
        float before = Scrolled(frame);
        float after = Math.Clamp(before + distance, 0, Overflow(frame));
        float height = frame.PageHeight * Scale(frame);
        Top = height > 0 ? after / height : 0;
        return Math.Abs(after - before) > Near;
    }

    /// <summary>Scrolls right by the distance (left, when it is negative), no further than the page goes; returns whether it moved.</summary>
    public bool ScrollAcross(Frame frame, float distance)
    {
        float before = ScrolledAcross(frame);
        float after = Math.Clamp(before + distance, 0, OverflowAcross(frame));
        float width = frame.PageWidth * Scale(frame);
        Left = width > 0 ? after / width : 0;
        return Math.Abs(after - before) > Near;
    }

    public void ToTop() => Top = 0;

    public void ToBottom() => Top = 1;

    public void ToLeft() => Left = 0;

    public void ToRight() => Left = 1;

    /// <summary>
    /// Zooms by the factor (in, when it is more than 1), keeping the point of the page at (x, y) in
    /// the area where it is, as a pinch or a turn of the wheel at the pointer should. It goes no
    /// smaller than the whole page, and no larger than <see cref="MostPixels"/>; zoomed out to the
    /// whole page, it is fitted whole again. Returns whether the zoom changed.
    /// </summary>
    public bool ZoomBy(Frame frame, double factor, float x, float y)
    {
        float whole = WholePage(frame);
        if (whole <= 0 || factor <= 0)
        {
            return false;
        }

        var before = Place(frame);
        double was = Scale(frame) / whole;
        double most = Math.Max(Math.Max(MostPixels / whole, frame.AreaWidth / frame.PageWidth / whole), 1);
        double zoom = Math.Clamp(was * factor, 1, most);
        if (Math.Abs(zoom - was) < 1e-6)
        {
            return false;
        }

        // the share of the page under the point, before, is under it after
        double across = before.Width > 0 ? (x - before.X) / before.Width : 0.5;
        double down = before.Height > 0 ? (y - before.Y) / before.Height : 0.5;
        (Fit, Zoom) = zoom - 1 < 1e-6 ? (Fit.Page, 1) : (Fit.Zoom, zoom);

        float width = frame.PageWidth * Scale(frame), height = frame.PageHeight * Scale(frame);
        Left = width > 0 ? Math.Clamp(across * width - x, 0, OverflowAcross(frame)) / width : 0;
        Top = height > 0 ? Math.Clamp(down * height - y, 0, Overflow(frame)) / height : 0;
        return true;
    }

    /// <summary>How far the page is scrolled down, in the area's units: never past where its bottom meets the area's.</summary>
    private float Scrolled(Frame frame) => Math.Min((float)(Top * frame.PageHeight * Scale(frame)), Overflow(frame));

    /// <summary>How far the page is scrolled across, in the area's units: never past where its right meets the area's.</summary>
    private float ScrolledAcross(Frame frame) => Math.Min((float)(Left * frame.PageWidth * Scale(frame)), OverflowAcross(frame));

    /// <summary>The scale at which the whole page fits the area.</summary>
    private static float WholePage(Frame frame) =>
        frame.PageWidth <= 0 || frame.PageHeight <= 0 ? 0 : Math.Min(frame.AreaWidth / frame.PageWidth, frame.AreaHeight / frame.PageHeight);

    /// <summary>The area's pixels to each of the page's.</summary>
    private float Scale(Frame frame)
    {
        if (frame.PageWidth <= 0 || frame.PageHeight <= 0)
        {
            return 0;
        }

        return Fit switch
        {
            Fit.Width => frame.AreaWidth / frame.PageWidth,
            Fit.Zoom => (float)(WholePage(frame) * Zoom),
            _ => WholePage(frame),
        };
    }
}
