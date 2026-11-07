namespace ComiCraft.Comics;

/// <summary>
/// Orders page names the way a reader expects: runs of digits compare by their value, so
/// "page2.jpg" comes before "page10.jpg". Everything else compares without regard to case.
/// </summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                // compare the numbers without their leading zeros: the longer one is larger
                var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                var numberY = y.AsSpan(startY, j - startY).TrimStart('0');
                int byValue = numberX.Length != numberY.Length
                    ? numberX.Length.CompareTo(numberY.Length)
                    : numberX.SequenceCompareTo(numberY);
                if (byValue != 0)
                {
                    return byValue;
                }
            }
            else
            {
                int byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (byChar != 0)
                {
                    return byChar;
                }

                i++;
                j++;
            }
        }

        int byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }
}
