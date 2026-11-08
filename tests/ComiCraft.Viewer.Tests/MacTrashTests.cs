using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Tests;

/// <summary>
/// The real Trash, on macOS: a file is moved there, found there, and then deleted from there, so
/// the tests leave nothing in it. On other systems there is nothing to test, and they pass.
/// </summary>
public sealed class MacTrashTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-trash-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void A_file_goes_to_the_trash()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        string path = Path.Combine(_folder, $"comicraft test {Guid.NewGuid():N}.cbr");
        File.WriteAllText(path, "a comic");

        var trashed = new MacTrash().Move(path);
        try
        {
            Assert.True(trashed.Moved, trashed.Failure);
            Assert.False(File.Exists(path));
            Assert.NotNull(trashed.Into);
            Assert.Contains(".Trash", trashed.Into);
            Assert.Equal("a comic", File.ReadAllText(trashed.Into));
        }
        finally
        {
            if (trashed.Into is { } into && File.Exists(into))
            {
                File.Delete(into);
            }
        }
    }

    [Fact]
    public void A_file_that_is_not_there_is_not_trashed_and_says_why()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var trashed = new MacTrash().Move(Path.Combine(_folder, "nowhere.cbr"));

        Assert.False(trashed.Moved);
        Assert.False(string.IsNullOrWhiteSpace(trashed.Failure));
    }
}
