namespace ComiCraft.Viewer.Cli;

/// <summary>
/// Where a command says what it did and asks what it needs to know: the console, or, in the
/// tests, text in memory. A question is asked only where someone can answer it, which is not
/// so when comicraft is run from a script with its input redirected.
/// </summary>
internal sealed class Terminal(TextReader input, TextWriter output, TextWriter error, bool canAsk)
{
    public static Terminal Console => new(System.Console.In, System.Console.Out, System.Console.Error, !System.Console.IsInputRedirected);

    public TextWriter Out { get; } = output;

    /// <summary>Whether there is someone to answer a question.</summary>
    public bool CanAsk { get; } = canAsk;

    /// <summary>Asks a question to be answered yes or no; anything but yes, Enter alone included, is no.</summary>
    public bool Ask(string question)
    {
        Out.Write($"{question} [y/N] ");
        string? answer = input.ReadLine();
        if (answer is null)
        {
            Out.WriteLine();
        }

        return answer?.Trim().ToLowerInvariant() is "y" or "yes";
    }

    /// <summary>
    /// Asks which of so many numbered choices, from 1; Enter alone is the first. Returns its
    /// number, or 0 when the answer is 0, or none of them, or there is no answer.
    /// </summary>
    public int Choose(string question, int choices)
    {
        Out.Write($"{question} [1-{choices}, Enter for 1, 0 for none] ");
        string? answer = input.ReadLine();
        if (answer is null)
        {
            Out.WriteLine();
            return 0;
        }

        answer = answer.Trim();
        if (answer.Length == 0)
        {
            return 1;
        }

        return int.TryParse(answer, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int chosen) && chosen <= choices
            ? chosen
            : 0;
    }

    public void Error(string message) => error.WriteLine($"comicraft: {message}");
}
