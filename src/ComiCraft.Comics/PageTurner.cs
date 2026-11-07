namespace ComiCraft.Comics;

/// <summary>Where a reader is in a comic: the current page, which never leaves the comic's pages.</summary>
public sealed class PageTurner(int pageCount)
{
    public int PageCount { get; } = pageCount >= 0 ? pageCount : throw new ArgumentOutOfRangeException(nameof(pageCount));

    /// <summary>The current page, counted from zero.</summary>
    public int Current { get; private set; }

    public bool IsFirst => Current == 0;

    public bool IsLast => Current >= PageCount - 1;

    /// <summary>Each of these returns whether the page changed.</summary>
    public bool Next() => GoTo(Current + 1);

    public bool Previous() => GoTo(Current - 1);

    public bool First() => GoTo(0);

    public bool Last() => GoTo(PageCount - 1);

    public bool GoTo(int page)
    {
        int clamped = Math.Clamp(page, 0, Math.Max(0, PageCount - 1));
        bool changed = clamped != Current;
        Current = clamped;
        return changed;
    }
}
