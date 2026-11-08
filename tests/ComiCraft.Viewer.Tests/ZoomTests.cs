using ComiCraft.Comics;

namespace ComiCraft.Viewer.Tests;

/// <summary>Zooming a page, and reading a page larger than the window across and then down.</summary>
public class ZoomTests
{
    // a page 1000 by 1500 in a window 1000 by 1000: whole, it is 2/3 of its size, 667 by 1000
    private static readonly Frame Tall = new(1000, 1500, 1000, 1000);

    // a square page in a square window half its size: zoomed twice, it is its own size, a screen
    // and as much again across and down
    private static readonly Frame Square = new(1000, 1000, 500, 500);

    private static void AssertPlaced(Placement expected, Placement actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01);
        Assert.Equal(expected.Y, actual.Y, 0.01);
        Assert.Equal(expected.Width, actual.Width, 0.01);
        Assert.Equal(expected.Height, actual.Height, 0.01);
    }

    /// <summary>The share of the page, across and down, at a point of the area.</summary>
    private static (double Across, double Down) Under(PageView view, Frame frame, float x, float y)
    {
        var place = view.Place(frame);
        return ((x - place.X) / place.Width, (y - place.Y) / place.Height);
    }

    [Fact]
    public void Zooming_in_keeps_the_point_zoomed_at_where_it_is()
    {
        var view = new PageView();
        var before = Under(view, Tall, 450, 300);

        Assert.True(view.ZoomBy(Tall, 2, 450, 300));

        var after = Under(view, Tall, 450, 300);
        Assert.Equal(Fit.Zoom, view.Fit);
        Assert.Equal(2, view.Zoom, 0.001);
        Assert.Equal(before.Across, after.Across, 0.001);
        Assert.Equal(before.Down, after.Down, 0.001);
    }

    [Fact]
    public void Zooming_in_at_the_middle_doubles_the_page_about_it()
    {
        var view = new PageView();

        view.ZoomBy(Tall, 2, 500, 500);

        AssertPlaced(new Placement(-1000 / 6f, -500, 4000 / 3f, 2000), view.Place(Tall));
    }

    [Fact]
    public void Near_an_edge_the_page_is_not_pulled_away_from_it()
    {
        var view = new PageView();

        // near the page's left, keeping the point where it is would show the window's background
        // to the left of the page; the page's left edge stays at the window's instead
        view.ZoomBy(Tall, 2, 200, 300);

        Assert.Equal(0, view.Place(Tall).X, 0.01);
    }

    [Fact]
    public void Nothing_zooms_out_past_the_whole_page()
    {
        var view = new PageView();

        Assert.False(view.ZoomBy(Tall, 0.5, 500, 500));

        Assert.Equal(Fit.Page, view.Fit);
    }

    [Fact]
    public void Zoomed_out_to_the_whole_page_it_is_fitted_whole_again()
    {
        var view = new PageView();
        view.ZoomBy(Tall, 3, 500, 500);

        view.ZoomBy(Tall, 0.1, 500, 500);

        Assert.Equal(Fit.Page, view.Fit);
        AssertPlaced(new PageView().Place(Tall), view.Place(Tall));
    }

    [Fact]
    public void Nothing_zooms_in_past_four_pixels_to_each_of_the_pages()
    {
        var view = new PageView();

        view.ZoomBy(Tall, 1000, 500, 500);

        Assert.Equal(4000, view.Place(Tall).Width, 0.01);
        Assert.False(view.ZoomBy(Tall, 2, 500, 500));
    }

    [Fact]
    public void Zooming_from_the_width_goes_on_from_its_size()
    {
        var view = new PageView { Fit = Fit.Width };

        view.ZoomBy(Tall, 1.25, 500, 500);

        Assert.Equal(1250, view.Place(Tall).Width, 0.01);
    }

    [Fact]
    public void The_zoom_holds_when_the_window_changes_size()
    {
        var view = new PageView();
        view.ZoomBy(Tall, 2, 500, 500);

        // in a window half as large, the whole page is half as large, and so is the zoomed one
        Assert.Equal(2000 / 3f, view.Place(new Frame(1000, 1500, 500, 500)).Width, 0.01);
    }

    [Fact]
    public void Scrolling_across_stops_at_the_pages_sides()
    {
        var view = Zoomed();

        Assert.True(view.ScrollAcross(Square, 300));
        Assert.True(view.ScrollAcross(Square, 300));
        Assert.True(view.AtRight(Square));
        Assert.Equal(-500, view.Place(Square).X, 0.01);
        Assert.False(view.ScrollAcross(Square, 100));

        view.ScrollAcross(Square, -2000);
        Assert.True(view.AtLeft(Square));
    }

    [Fact]
    public void A_page_narrower_than_the_window_is_centred_across_and_does_not_scroll_across()
    {
        var view = new PageView { Fit = Fit.Width };
        var narrow = new Frame(1000, 1500, 1000, 1000);

        Assert.False(view.ScrollAcross(narrow, 100));
        Assert.True(view.AtLeft(narrow) && view.AtRight(narrow));
    }

    /// <summary>The square page, zoomed to its own size, at its top left.</summary>
    private static PageView Zoomed()
    {
        var view = new PageView();
        view.ZoomBy(Square, 2, 250, 250);
        view.ToTop();
        view.ToLeft();
        return view;
    }

    private static Reading Comic(int pages) => new(new PageTurner(pages), Zoomed());

    [Fact]
    public void A_screen_forward_reads_across_then_down_then_on_to_the_next_page()
    {
        var reading = Comic(2);
        var view = reading.View;

        // across the first row: 450, then the last 50 to the right
        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Square));
        Assert.Equal(-450, view.Place(Square).X, 0.01);
        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Square));
        Assert.True(view.AtRight(Square));

        // then back to the left, a screen down
        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Square));
        Assert.True(view.AtLeft(Square));
        Assert.Equal(-450, view.Place(Square).Y, 0.01);

        // across, down to the bottom, across, and on
        reading.ScreenForward(Square);
        reading.ScreenForward(Square);
        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Square));
        Assert.True(view.AtBottom(Square) && view.AtLeft(Square));
        reading.ScreenForward(Square);
        reading.ScreenForward(Square);
        Assert.True(view.AtBottom(Square) && view.AtRight(Square));

        Assert.Equal(Moved.Turned, reading.ScreenForward(Square));
        Assert.Equal(1, reading.Turner.Current);
        Assert.True(view.AtTop(Square) && view.AtLeft(Square));
        Assert.Equal(Fit.Zoom, view.Fit);
    }

    [Fact]
    public void A_screen_back_reads_the_other_way_to_the_end_of_the_page_before()
    {
        var reading = Comic(2);
        reading.NextPage();

        Assert.Equal(Moved.Turned, reading.ScreenBack(Square));

        Assert.Equal(0, reading.Turner.Current);
        Assert.True(reading.View.AtBottom(Square) && reading.View.AtRight(Square));

        // back across the last row, then up a screen at the right
        reading.ScreenBack(Square);
        reading.ScreenBack(Square);
        Assert.True(reading.View.AtLeft(Square));
        Assert.Equal(Moved.Scrolled, reading.ScreenBack(Square));
        Assert.True(reading.View.AtRight(Square));
        Assert.False(reading.View.AtBottom(Square));
    }

    [Fact]
    public void A_held_key_stops_at_the_end_of_a_row()
    {
        var reading = Comic(2);
        reading.View.ToRight();

        Assert.Equal(Moved.Nothing, reading.ScreenForward(Square, repeat: true));
        Assert.True(reading.View.AtTop(Square));
        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Square));
    }

    [Fact]
    public void Right_and_left_step_across_a_wide_page_and_turn_at_its_sides()
    {
        var reading = Comic(2);

        Assert.Equal(Moved.Scrolled, reading.StepRight(Square));
        Assert.Equal(-50, reading.View.Place(Square).X, 0.01);
        reading.View.ToRight();
        Assert.Equal(Moved.Nothing, reading.StepRight(Square, repeat: true));
        Assert.Equal(Moved.Turned, reading.StepRight(Square));
        Assert.Equal(1, reading.Turner.Current);

        Assert.Equal(Moved.Turned, reading.StepLeft(Square));
        Assert.Equal(0, reading.Turner.Current);
        Assert.True(reading.View.AtTop(Square) && reading.View.AtLeft(Square));
    }

    [Fact]
    public void Where_the_page_fits_across_right_and_left_turn_it_at_once()
    {
        var reading = new Reading(new PageTurner(3), new PageView { Fit = Fit.Width });
        var frame = new Frame(1000, 1500, 1000, 1000);

        Assert.Equal(Moved.Turned, reading.StepRight(frame));
        Assert.Equal(Moved.Turned, reading.StepLeft(frame));
        Assert.Equal(0, reading.Turner.Current);
    }

    [Fact]
    public void Panning_moves_across_and_down_and_never_turns()
    {
        var reading = Comic(2);

        Assert.Equal(Moved.Scrolled, reading.Pan(Square, 100, 200));
        AssertPlaced(new Placement(-100, -200, 1000, 1000), reading.View.Place(Square));

        Assert.Equal(Moved.Scrolled, reading.Pan(Square, 5000, 5000));
        Assert.Equal(Moved.Nothing, reading.Pan(Square, 5000, 5000));
        Assert.Equal(0, reading.Turner.Current);
    }
}
