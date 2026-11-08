using ComiCraft.Viewer.Platform;

namespace ComiCraft.Viewer.Tests;

/// <summary>
/// The Recycle Bin. That a delete Windows would not recycle is refused is tested everywhere; the
/// real Bin only on Windows, where a file is recycled, found there, and then taken out of it again
/// (the file as $R… and its record as $I…, beside it), so the tests leave nothing in it.
/// </summary>
public sealed class WindowsTrashTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("comicraft-recycle-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Theory]
    [InlineData(0x80u, false)]          // TSF_DELETE_RECYCLE_IF_POSSIBLE: recycled
    [InlineData(0x80u | 0x40u, false)]
    [InlineData(0x0u, true)]            // without it, the file would be destroyed
    [InlineData(0x40u, true)]
    public void A_delete_that_would_not_recycle_is_known(uint flags, bool destroys)
    {
        Assert.Equal(destroys, RecycleBin.WouldDestroy(flags));
    }

    [Theory]
    [InlineData(0x00000000, false)]     // S_OK
    [InlineData(0x00270008, false)]     // COPYENGINE_S_DONT_PROCESS_CHILDREN: what recycling a file reports
    [InlineData(unchecked((int)0x80004004), true)] // E_ABORT
    [InlineData(unchecked((int)0x80270000), true)] // COPYENGINE_E_USER_CANCELLED
    public void Only_an_error_is_a_failure_not_every_result_but_S_OK(int hresult, bool failed)
    {
        Assert.Equal(failed, RecycleBin.Failed(hresult));
    }

    [Fact]
    public void A_file_goes_to_the_recycle_bin()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string path = Path.Combine(_folder, $"comicraft test {Guid.NewGuid():N}.cbr");
        File.WriteAllText(path, "a comic");

        var trashed = new WindowsTrash().Move(path);
        try
        {
            Assert.True(trashed.Moved, trashed.Failure);
            Assert.False(File.Exists(path));
            Assert.NotNull(trashed.Into);
            Assert.Contains("$Recycle.Bin", trashed.Into, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("a comic", File.ReadAllText(trashed.Into));
        }
        finally
        {
            if (trashed.Into is { } into && File.Exists(into))
            {
                File.Delete(into);
                string record = Path.Combine(Path.GetDirectoryName(into)!, "$I" + Path.GetFileName(into)[2..]);
                if (File.Exists(record))
                {
                    File.Delete(record);
                }
            }
        }
    }

    [Fact]
    public void A_file_that_is_not_there_is_not_recycled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.False(new WindowsTrash().Move(Path.Combine(_folder, "nowhere.cbr")).Moved);
    }
}
