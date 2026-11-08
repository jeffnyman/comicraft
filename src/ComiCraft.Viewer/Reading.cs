using ComiCraft.Comics;

namespace ComiCraft.Viewer;

/// <summary>What a reader's action did: nothing, moved within the page, or turned to another page.</summary>
internal enum Moved { Nothing, Scrolled, Turned }

/// <summary>
/// Moving through a comic: within a page when it is larger than the window, and from page to
/// page. A page is read as it is in print, across and then down: a screen forward goes across
/// the page, then back to its left a screen further down, and past its end turns to the top of
/// the next; a screen back goes the other way, and past the start of a page turns to the end of
/// the one before, so the reader picks up where they would in print. A held key, repeating, stops
/// at the end of a row, or of a page, that it has been scrolling, rather than running on through
/// the comic; where pages fit the window, it turns them as before.
/// </summary>
internal sealed class Reading(PageTurner turner, PageView view)
{
    /// <summary>A screen, less a little, so the reader keeps the line they were reading in sight.</summary>
    private const float ScreenStep = 0.9f;

    /// <summary>A step of the arrow keys, as a share of the screen.</summary>
    private const float ArrowStep = 0.1f;

    public PageTurner Turner { get; } = turner;

    public PageView View { get; } = view;

    /// <summary>A screen across the page; at its right, a screen down at its left; at its end, the next page.</summary>
    public Moved ScreenForward(Frame frame, bool repeat = false)
    {
        if (!View.AtRight(frame))
        {
            return Across(frame, frame.AreaWidth * ScreenStep);
        }

        if (!View.AtBottom(frame))
        {
            if (repeat && View.OverflowAcross(frame) > 0)
            {
                return Moved.Nothing; // held at the end of a row
            }

            View.ToLeft();
            View.Scroll(frame, frame.AreaHeight * ScreenStep);
            return Moved.Scrolled;
        }

        return Held(frame, repeat) ? Moved.Nothing : NextPage();
    }

    /// <summary>A screen back across the page; at its left, a screen up at its right; at its start, the end of the page before.</summary>
    public Moved ScreenBack(Frame frame, bool repeat = false)
    {
        if (!View.AtLeft(frame))
        {
            return Across(frame, -frame.AreaWidth * ScreenStep);
        }

        if (!View.AtTop(frame))
        {
            if (repeat && View.OverflowAcross(frame) > 0)
            {
                return Moved.Nothing;
            }

            View.ToRight();
            View.Scroll(frame, -frame.AreaHeight * ScreenStep);
            return Moved.Scrolled;
        }

        return Held(frame, repeat) ? Moved.Nothing : ToEndOfPreviousPage();
    }

    /// <summary>A step down the page; at its bottom, the next page.</summary>
    public Moved StepForward(Frame frame, bool repeat = false)
    {
        if (!View.AtBottom(frame))
        {
            return Scroll(frame, frame.AreaHeight * ArrowStep);
        }

        return Held(frame, repeat) ? Moved.Nothing : NextPage();
    }

    /// <summary>A step up the page; at its top, the end of the page before.</summary>
    public Moved StepBack(Frame frame, bool repeat = false)
    {
        if (!View.AtTop(frame))
        {
            return Scroll(frame, -frame.AreaHeight * ArrowStep);
        }

        return Held(frame, repeat) ? Moved.Nothing : ToEndOfPreviousPage();
    }

    /// <summary>A step right across a page wider than the window; at its right, or on a page that fits, the next page.</summary>
    public Moved StepRight(Frame frame, bool repeat = false)
    {
        if (!View.AtRight(frame))
        {
            return Across(frame, frame.AreaWidth * ArrowStep);
        }

        return Held(frame, repeat) ? Moved.Nothing : NextPage();
    }

    /// <summary>A step left across a page wider than the window; at its left, or on a page that fits, the page before, at its top.</summary>
    public Moved StepLeft(Frame frame, bool repeat = false)
    {
        if (!View.AtLeft(frame))
        {
            return Across(frame, -frame.AreaWidth * ArrowStep);
        }

        return Held(frame, repeat) ? Moved.Nothing : PreviousPage();
    }

    /// <summary>Scrolls by the distance, down when it is positive; never turns the page.</summary>
    public Moved Scroll(Frame frame, float distance) => View.Scroll(frame, distance) ? Moved.Scrolled : Moved.Nothing;

    /// <summary>Scrolls across and down at once, as the wheel and a drag do; never turns the page.</summary>
    public Moved Pan(Frame frame, float across, float down)
    {
        bool moved = View.ScrollAcross(frame, across);
        moved |= View.Scroll(frame, down);
        return moved ? Moved.Scrolled : Moved.Nothing;
    }

    public Moved NextPage() => TurnToTop(Turner.Next());

    public Moved PreviousPage() => TurnToTop(Turner.Previous());

    public Moved FirstPage() => TurnToTop(Turner.First());

    public Moved LastPage() => TurnToTop(Turner.Last());

    private Moved Across(Frame frame, float distance) => View.ScrollAcross(frame, distance) ? Moved.Scrolled : Moved.Nothing;

    /// <summary>Whether a repeating key is held at the end of a page that scrolls, and so should stop there.</summary>
    private bool Held(Frame frame, bool repeat) => repeat && (View.Overflow(frame) > 0 || View.OverflowAcross(frame) > 0);

    private Moved ToEndOfPreviousPage()
    {
        if (!Turner.Previous())
        {
            return Moved.Nothing;
        }

        View.ToBottom();
        View.ToRight();
        return Moved.Turned;
    }

    private Moved TurnToTop(bool turned)
    {
        if (!turned)
        {
            return Moved.Nothing;
        }

        View.ToTop();
        View.ToLeft();
        return Moved.Turned;
    }
}
