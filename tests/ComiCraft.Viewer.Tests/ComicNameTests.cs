using ComiCraft.Viewer.Lookup;

namespace ComiCraft.Viewer.Tests;

/// <summary>Which issue a comic is, as its file name says.</summary>
public class ComicNameTests
{
    [Theory]
    [InlineData("Marvel Super-Heroes Secret Wars 01 (of 12) (1984).cbr", "Marvel Super-Heroes Secret Wars", "01", 1984, 12)]
    [InlineData("Troll 001 (1993).cbz", "Troll", "001", 1993, null)]
    [InlineData("Troll #1.cbz", "Troll", "1", null, null)]
    [InlineData("Spawn 1.5 (1993) (Digital) [Scanner].cbz", "Spawn", "1.5", 1993, null)]
    [InlineData("Batman_-_Year_One_02_(1987).cbr", "Batman - Year One", "02", 1987, null)]
    [InlineData("2000 AD 0042 (1977).cbz", "2000 AD", "0042", 1977, null)]
    [InlineData("Daredevil 181 (of 191) (1982).cbz", "Daredevil", "181", 1982, 191)]
    public void A_name_says_the_series_the_number_and_the_year(string file, string series, string number, int? year, int? count)
    {
        Assert.Equal(new ComicName(series, number, year, count), ComicName.Parse(file));
    }

    [Theory]
    [InlineData("Watchmen.cbz")]
    [InlineData("(1986).cbz")]
    [InlineData("042.cbz")]
    public void A_name_without_a_series_and_a_number_says_nothing(string file)
    {
        Assert.Null(ComicName.Parse(file));
    }

    [Theory]
    [InlineData("001", "1")]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("000", "0")]
    [InlineData("1.5", "1.5")]
    [InlineData("1/2", "1/2")]
    [InlineData("Annual 1", "annual 1")]
    public void Numbers_compare_without_their_padding(string number, string key)
    {
        Assert.Equal(key, ComicName.Key(number));
    }
}
