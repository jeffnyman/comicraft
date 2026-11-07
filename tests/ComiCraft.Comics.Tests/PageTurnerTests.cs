using ComiCraft.Comics;

namespace ComiCraft.Comics.Tests;

public class PageTurnerTests
{
    [Fact]
    public void Starts_on_the_first_page()
    {
        var turner = new PageTurner(3);
        Assert.Equal(0, turner.Current);
        Assert.True(turner.IsFirst);
    }

    [Fact]
    public void Turns_forward_and_back()
    {
        var turner = new PageTurner(3);
        Assert.True(turner.Next());
        Assert.True(turner.Next());
        Assert.Equal(2, turner.Current);
        Assert.True(turner.Previous());
        Assert.Equal(1, turner.Current);
    }

    [Fact]
    public void Stops_at_either_end()
    {
        var turner = new PageTurner(3);
        Assert.False(turner.Previous());
        turner.Last();
        Assert.True(turner.IsLast);
        Assert.False(turner.Next());
        Assert.Equal(2, turner.Current);
    }

    [Fact]
    public void GoTo_keeps_within_the_comic()
    {
        var turner = new PageTurner(5);
        turner.GoTo(99);
        Assert.Equal(4, turner.Current);
        turner.GoTo(-1);
        Assert.Equal(0, turner.Current);
    }
}
