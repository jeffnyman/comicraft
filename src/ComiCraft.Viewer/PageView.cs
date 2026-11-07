namespace ComiCraft.Viewer;

/// <summary>How a page is fitted to the window.</summary>
internal enum Fit
{
    /// <summary>The whole page is seen at once.</summary>
    Page,

    /// <summary>The page fills the window's width, and is scrolled when it is taller than the window.</summary>
    Width,
}

/// <summary>A page's size and the size of the area it is shown in, in the same units (pixels).</summary>
internal readonly record struct Frame(float PageWidth, float PageHeight, float AreaWidth, float AreaHeight);

/// <summary>Where the page is drawn in the area; it can start above the area when scrolled.</summary>
internal readonly record struct Placement(float X, float Y, float Width, float Height);

/// <summary>
/// How a page is seen: how it is fitted, and how far down it the reader has scrolled. The place
/// is kept as a share of the page's height, so it holds when the window changes size, and so
/// "the bottom" means the bottom of whichever page is shown next.
/// </summary>
internal sealed class PageView
{
    // half a pixel: closer than that to the end of the page is at the end of it
    private const float Near = 0.5f;

    public Fit Fit { get; set; } = Fit.Page;

    /// <summary>How far down the page the top of the area is, from 0 (its top) to 1 (past its end, kept at its bottom).</summary>
    public double Top { get; private set; }

    public Placement Place(Frame frame)
    {
        float height = ScaledHeight(frame);
        float width = frame.PageHeight > 0 ? frame.PageWidth * (height / frame.PageHeight) : 0;
        float x = (frame.AreaWidth - width) / 2;
        float y = height <= frame.AreaHeight ? (frame.AreaHeight - height) / 2 : -Scrolled(frame);
        return new Placement(x, y, width, height);
    }

    /// <summary>How much of the page is below the area when it is scrolled to the top; nothing when it fits.</summary>
    public float Overflow(Frame frame) => Math.Max(0, ScaledHeight(frame) - frame.AreaHeight);

    public bool AtTop(Frame frame) => Scrolled(frame) <= Near;

    public bool AtBottom(Frame frame) => Scrolled(frame) >= Overflow(frame) - Near;

    /// <summary>Scrolls down by the distance (up, when it is negative), no further than the page goes; returns whether it moved.</summary>
    public bool Scroll(Frame frame, float distance)
    {
        float before = Scrolled(frame);
        float after = Math.Clamp(before + distance, 0, Overflow(frame));
        float height = ScaledHeight(frame);
        Top = height > 0 ? after / height : 0;
        return Math.Abs(after - before) > Near;
    }

    public void ToTop() => Top = 0;

    public void ToBottom() => Top = 1;

    /// <summary>How far the page is scrolled, in the area's units: never past where its bottom meets the area's.</summary>
    private float Scrolled(Frame frame) => Math.Min((float)(Top * ScaledHeight(frame)), Overflow(frame));

    private float ScaledHeight(Frame frame)
    {
        if (frame.PageWidth <= 0 || frame.PageHeight <= 0)
        {
            return 0;
        }

        float scale = Fit == Fit.Width
            ? frame.AreaWidth / frame.PageWidth
            : Math.Min(frame.AreaWidth / frame.PageWidth, frame.AreaHeight / frame.PageHeight);
        return frame.PageHeight * scale;
    }
}
