using ComiCraft.Comics;

namespace ComiCraft.Viewer.Tests;

public class ReadingTests
{
    // every page 1000x1500 on a 1600x1000 screen: fitted to the width, 1400 to scroll through
    // each; a screen forward is 900, a step 100
    private static readonly Frame Landscape = new(1000, 1500, 1600, 1000);

    private static Reading Comic(int pages, Fit fit) => new(new PageTurner(pages), new PageView { Fit = fit });

    [Fact]
    public void A_screen_forward_scrolls_down_the_page_and_then_turns_it()
    {
        var reading = Comic(3, Fit.Width);

        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Landscape));
        Assert.Equal(Moved.Scrolled, reading.ScreenForward(Landscape));
        Assert.True(reading.View.AtBottom(Landscape));
        Assert.Equal(Moved.Turned, reading.ScreenForward(Landscape));
        Assert.Equal(1, reading.Turner.Current);
        Assert.True(reading.View.AtTop(Landscape));
    }

    [Fact]
    public void A_screen_back_from_the_top_of_a_page_goes_to_the_bottom_of_the_one_before()
    {
        var reading = Comic(3, Fit.Width);
        reading.NextPage();

        Assert.Equal(Moved.Turned, reading.ScreenBack(Landscape));
        Assert.Equal(0, reading.Turner.Current);
        Assert.True(reading.View.AtBottom(Landscape));
    }

    [Fact]
    public void A_step_is_smaller_than_a_screen()
    {
        var reading = Comic(3, Fit.Width);

        reading.StepForward(Landscape);
        Assert.Equal(-100, reading.View.Place(Landscape).Y, 0.01);
        reading.StepBack(Landscape);
        Assert.True(reading.View.AtTop(Landscape));
    }

    [Fact]
    public void A_held_key_stops_at_the_end_of_a_page_it_scrolled()
    {
        var reading = Comic(3, Fit.Width);
        reading.View.ToBottom();

        Assert.Equal(Moved.Nothing, reading.StepForward(Landscape, repeat: true));
        Assert.Equal(0, reading.Turner.Current);
        Assert.Equal(Moved.Turned, reading.StepForward(Landscape));
    }

    [Fact]
    public void A_held_key_at_the_top_does_not_go_back_a_page()
    {
        var reading = Comic(3, Fit.Width);
        reading.NextPage();

        Assert.Equal(Moved.Nothing, reading.StepBack(Landscape, repeat: true));
        Assert.Equal(1, reading.Turner.Current);
    }

    [Fact]
    public void Where_pages_fit_every_press_turns_one_held_or_not()
    {
        var reading = Comic(3, Fit.Page);

        Assert.Equal(Moved.Turned, reading.StepForward(Landscape));
        Assert.Equal(Moved.Turned, reading.StepForward(Landscape, repeat: true));
        Assert.Equal(2, reading.Turner.Current);
        Assert.Equal(Moved.Turned, reading.ScreenBack(Landscape, repeat: true));
        Assert.Equal(1, reading.Turner.Current);
    }

    [Fact]
    public void The_wheel_scrolls_but_never_turns()
    {
        var reading = Comic(3, Fit.Width);

        Assert.Equal(Moved.Scrolled, reading.Scroll(Landscape, 5000));
        Assert.Equal(Moved.Nothing, reading.Scroll(Landscape, 5000));
        Assert.Equal(0, reading.Turner.Current);
    }

    [Fact]
    public void Turning_a_whole_page_starts_at_its_top()
    {
        var reading = Comic(3, Fit.Width);
        reading.ScreenForward(Landscape);

        Assert.Equal(Moved.Turned, reading.NextPage());
        Assert.True(reading.View.AtTop(Landscape));
        reading.View.ToBottom();
        Assert.Equal(Moved.Turned, reading.PreviousPage());
        Assert.True(reading.View.AtTop(Landscape));
    }

    [Fact]
    public void Nothing_moves_past_the_ends_of_the_comic()
    {
        var reading = Comic(2, Fit.Width);

        Assert.Equal(Moved.Nothing, reading.ScreenBack(Landscape));
        Assert.True(reading.View.AtTop(Landscape));

        reading.LastPage();
        reading.View.ToBottom();
        Assert.Equal(Moved.Nothing, reading.ScreenForward(Landscape));
        Assert.Equal(1, reading.Turner.Current);
    }
}
