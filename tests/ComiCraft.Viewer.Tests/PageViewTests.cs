namespace ComiCraft.Viewer.Tests;

public class PageViewTests
{
    // a portrait page, 1000x1500, on a landscape screen, 1600x1000: fitted to the width it is
    // 1600x2400, with 1400 below the screen to scroll through
    private static readonly Frame Landscape = new(1000, 1500, 1600, 1000);

    private static PageView Width() => new() { Fit = Fit.Width };

    /// <summary>Placements are worked out in floats, so are compared to a hundredth of a pixel.</summary>
    private static void AssertPlaced(Placement expected, Placement actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01);
        Assert.Equal(expected.Y, actual.Y, 0.01);
        Assert.Equal(expected.Width, actual.Width, 0.01);
        Assert.Equal(expected.Height, actual.Height, 0.01);
    }

    [Fact]
    public void A_whole_page_is_centred_with_room_at_the_sides()
    {
        var place = new PageView().Place(Landscape);

        Assert.Equal(1000, place.Height, 0.01);
        Assert.Equal(666.67, place.Width, 0.01);
        Assert.Equal(466.67, place.X, 0.01);
        Assert.Equal(0, place.Y);
        Assert.Equal(0, new PageView().Overflow(Landscape));
    }

    [Fact]
    public void Fitted_to_the_width_the_page_fills_it_and_runs_below()
    {
        var view = Width();
        var place = view.Place(Landscape);

        AssertPlaced(new Placement(0, 0, 1600, 2400), place);
        Assert.Equal(1400, view.Overflow(Landscape));
        Assert.True(view.AtTop(Landscape));
        Assert.False(view.AtBottom(Landscape));
    }

    [Fact]
    public void Scrolling_moves_the_page_up_and_stops_at_its_bottom()
    {
        var view = Width();

        Assert.True(view.Scroll(Landscape, 900));
        Assert.Equal(-900, view.Place(Landscape).Y, 0.01);
        Assert.True(view.Scroll(Landscape, 900));
        Assert.Equal(-1400, view.Place(Landscape).Y, 0.01);
        Assert.True(view.AtBottom(Landscape));
        Assert.False(view.Scroll(Landscape, 900));
    }

    [Fact]
    public void Scrolling_up_stops_at_the_top()
    {
        var view = Width();
        view.Scroll(Landscape, 100);

        Assert.True(view.Scroll(Landscape, -500));
        Assert.True(view.AtTop(Landscape));
        Assert.False(view.Scroll(Landscape, -500));
    }

    [Fact]
    public void The_bottom_is_the_bottom_of_whatever_page_is_shown()
    {
        var view = Width();
        view.ToBottom();

        Assert.Equal(-1400, view.Place(Landscape).Y, 0.01);
        Assert.Equal(-200, view.Place(new Frame(1000, 1200, 1000, 1000)).Y, 0.01);
    }

    [Fact]
    public void The_place_on_the_page_holds_when_the_window_changes_size()
    {
        var view = Width();
        view.Scroll(Landscape, 600); // a quarter of the way down the page

        // narrower: the page is 1200x1800, and a quarter of the way down it is 450
        Assert.Equal(-450, view.Place(new Frame(1000, 1500, 1200, 1000)).Y, 0.01);
    }

    [Fact]
    public void A_page_shorter_than_the_window_is_centred_and_does_not_scroll()
    {
        var view = Width();
        var portrait = new Frame(1000, 1500, 600, 1200);

        AssertPlaced(new Placement(0, 150, 600, 900), view.Place(portrait));
        Assert.False(view.Scroll(portrait, 100));
        Assert.True(view.AtTop(portrait) && view.AtBottom(portrait));
    }

    [Fact]
    public void A_page_that_could_not_be_shown_takes_no_room()
    {
        var nothing = new Frame(0, 0, 1600, 1000);

        Assert.Equal(0, Width().Place(nothing).Height);
        Assert.False(Width().Scroll(nothing, 100));
    }
}
