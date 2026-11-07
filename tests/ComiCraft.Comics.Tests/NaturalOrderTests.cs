using ComiCraft.Comics;

namespace ComiCraft.Comics.Tests;

public class NaturalOrderTests
{
    [Fact]
    public void Numbers_compare_by_value()
    {
        string[] names = ["page10.jpg", "page2.jpg", "page1.jpg"];
        Assert.Equal(["page1.jpg", "page2.jpg", "page10.jpg"], names.Order(NaturalOrder.Instance));
    }

    [Fact]
    public void Leading_zeros_do_not_change_the_order()
    {
        string[] names = ["10.jpg", "009.jpg", "1.jpg"];
        Assert.Equal(["1.jpg", "009.jpg", "10.jpg"], names.Order(NaturalOrder.Instance));
    }

    [Fact]
    public void Case_is_ignored()
    {
        string[] names = ["b.jpg", "A.jpg", "c.jpg"];
        Assert.Equal(["A.jpg", "b.jpg", "c.jpg"], names.Order(NaturalOrder.Instance));
    }

    [Fact]
    public void A_cover_numbered_zero_comes_first()
    {
        string[] names = ["Issue 1/02.jpg", "Issue 1/00Front Cover.jpg", "Issue 1/01.jpg"];
        Assert.Equal(["Issue 1/00Front Cover.jpg", "Issue 1/01.jpg", "Issue 1/02.jpg"], names.Order(NaturalOrder.Instance));
    }
}
