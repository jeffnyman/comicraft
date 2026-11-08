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

    public void Error(string message) => error.WriteLine($"comicraft: {message}");
}
