namespace ComiCraft.Comics;

/// <summary>Where a reader is in a comic: the current page, which never leaves the comic's pages.</summary>
public sealed class PageTurner(int pageCount)
{
    /// <summary>How many pages the comic has; none at all is allowed, and the current page then stays at zero.</summary>
    public int PageCount { get; } = pageCount >= 0 ? pageCount : throw new ArgumentOutOfRangeException(nameof(pageCount));

    /// <summary>The current page, counted from zero.</summary>
    public int Current { get; private set; }

    /// <summary>Whether the current page is the first.</summary>
    public bool IsFirst => Current == 0;

    /// <summary>Whether the current page is the last.</summary>
    public bool IsLast => Current >= PageCount - 1;

    /// <summary>To the next page. This and the moves below return whether the page changed.</summary>
    public bool Next() => GoTo(Current + 1);

    /// <summary>To the page before.</summary>
    public bool Previous() => GoTo(Current - 1);

    /// <summary>To the first page.</summary>
    public bool First() => GoTo(0);

    /// <summary>To the last page.</summary>
    public bool Last() => GoTo(PageCount - 1);

    /// <summary>To <paramref name="page"/>, counted from zero; a page outside the comic goes to the nearest there is.</summary>
    public bool GoTo(int page)
    {
        int clamped = Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        bool changed = clamped != Current;
        Current = clamped;
        return changed;
    }
}
