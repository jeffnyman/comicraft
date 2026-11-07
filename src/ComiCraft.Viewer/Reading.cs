using ComiCraft.Comics;

namespace ComiCraft.Viewer;

/// <summary>What a reader's action did: nothing, moved within the page, or turned to another page.</summary>
internal enum Moved { Nothing, Scrolled, Turned }

/// <summary>
/// Moving through a comic: within a page when it is taller than the window, and from page to
/// page. Scrolling forward past the end of a page turns to the top of the next; scrolling back
/// past the start of a page turns to the bottom of the one before, so the reader picks up where
/// they would in print. A held key, repeating, stops at the end of a page it has been scrolling
/// rather than running on through the comic; where pages fit the window, it turns them as before.
/// </summary>
internal sealed class Reading(PageTurner turner, PageView view)
{
    /// <summary>A screen, less a little, so the reader keeps the line they were reading in sight.</summary>
    private const float ScreenStep = 0.9f;

    /// <summary>A step of the arrow keys, as a share of the screen.</summary>
    private const float ArrowStep = 0.1f;

    public PageTurner Turner { get; } = turner;

    public PageView View { get; } = view;

    public Moved ScreenForward(Frame frame, bool repeat = false) => Forward(frame, frame.AreaHeight * ScreenStep, repeat);

    public Moved ScreenBack(Frame frame, bool repeat = false) => Back(frame, frame.AreaHeight * ScreenStep, repeat);

    public Moved StepForward(Frame frame, bool repeat = false) => Forward(frame, frame.AreaHeight * ArrowStep, repeat);

    public Moved StepBack(Frame frame, bool repeat = false) => Back(frame, frame.AreaHeight * ArrowStep, repeat);

    /// <summary>Scrolls by the distance, down when it is positive; never turns the page.</summary>
    public Moved Scroll(Frame frame, float distance) => View.Scroll(frame, distance) ? Moved.Scrolled : Moved.Nothing;

    public Moved NextPage() => TurnToTop(Turner.Next());

    public Moved PreviousPage() => TurnToTop(Turner.Previous());

    public Moved FirstPage() => TurnToTop(Turner.First());

    public Moved LastPage() => TurnToTop(Turner.Last());

    private Moved Forward(Frame frame, float distance, bool repeat)
    {
        if (!View.AtBottom(frame))
        {
            return Scroll(frame, distance);
        }

        return Held(frame, repeat) ? Moved.Nothing : NextPage();
    }

    private Moved Back(Frame frame, float distance, bool repeat)
    {
        if (!View.AtTop(frame))
        {
            return Scroll(frame, -distance);
        }

        if (Held(frame, repeat) || !Turner.Previous())
        {
            return Moved.Nothing;
        }

        View.ToBottom();
        return Moved.Turned;
    }

    /// <summary>Whether a repeating key is held at the end of a page that scrolls, and so should stop there.</summary>
    private bool Held(Frame frame, bool repeat) => repeat && View.Overflow(frame) > 0;

    private Moved TurnToTop(bool turned)
    {
        if (!turned)
        {
            return Moved.Nothing;
        }

        View.ToTop();
        return Moved.Turned;
    }
}
