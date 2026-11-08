using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ComiCraft.Viewer.Platform;

/// <summary>What Windows says of a delete it is about to do, which can be known on any system.</summary>
internal static class RecycleBin
{
    /// <summary>A TRANSFER_SOURCE_FLAGS flag: the delete is to recycle the item, not destroy it.</summary>
    internal const uint TSF_DELETE_RECYCLE_IF_POSSIBLE = 0x80;

    /// <summary>Whether a delete with these flags would destroy the item rather than recycle it.</summary>
    internal static bool WouldDestroy(uint flags) => (flags & TSF_DELETE_RECYCLE_IF_POSSIBLE) == 0;
}

/// <summary>
/// The Recycle Bin on Windows, by the Shell's IFileOperation, as Explorer moves files there: to the
/// Recycle Bin of the drive the file is on, from which it can be restored.
/// <para>
/// Windows deletes a file for good, without a word, where it cannot recycle it: on a drive with no
/// Recycle Bin (a network share, some removable drives), one set not to keep deleted files, or for a
/// file too large for the Bin. So before each delete, Windows is asked (by PreDeleteItem, on the
/// operation's progress sink) whether it means to recycle the file; where it does not, the delete is
/// cancelled, as an error from PreDeleteItem cancels it. Afterwards, PostDeleteItem says where the
/// file went in the Bin, or that it went nowhere, and that too is checked.
/// </para>
/// <para>
/// IFileOperation is only to be used from a single-threaded apartment, so it is used from a thread
/// of its own that is one.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTrash : ITrash
{
    // the operation's flags (FOF_ from shellapi.h, FOFX_ from shobjidl.h): to the Recycle Bin,
    // with an undo record, and without any dialog, a command line having no one to show one to
    private const uint FOF_SILENT = 0x0004, FOF_NOCONFIRMATION = 0x0010, FOF_ALLOWUNDO = 0x0040,
        FOF_NOERRORUI = 0x0400, FOFX_RECYCLEONDELETE = 0x00080000, FOFX_EARLYFAILURE = 0x00100000,
        FOFX_ADDUNDORECORD = 0x20000000;

    private const uint Flags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI
        | FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE | FOFX_ADDUNDORECORD;

    private const int S_OK = 0, E_ABORT = unchecked((int)0x80004004);

    private static readonly Guid FileOperationClass = new("3ad05575-8857-4850-9277-11b85bdb8e09");

    public bool IsAvailable => true;

    public Trashed Move(string path)
    {
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            full = full[4..]; // the Shell parses paths as Explorer shows them, without the prefix
        }

        if (!File.Exists(full))
        {
            return Trashed.Not($"{full} is not there.");
        }

        Trashed result = default;
        var thread = new Thread(() => result = MoveOnThisThread(full)) { IsBackground = true, Name = "Recycle Bin" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result;
    }

    private static Trashed MoveOnThisThread(string full)
    {
        IFileOperation? operation = null;
        IShellItem? item = null;
        var sink = new Sink();
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(FileOperationClass, throwOnError: true)!)!;
            Check(operation.SetOperationFlags(Flags), "SetOperationFlags");
            Check(SHCreateItemFromParsingName(full, IntPtr.Zero, typeof(IShellItem).GUID, out item), "SHCreateItemFromParsingName");
            Check(operation.DeleteItem(item, sink), "DeleteItem");

            int performed = operation.PerformOperations();
            if (sink.WouldDestroy)
            {
                return Trashed.Not("Windows would have deleted it permanently: its drive has no Recycle Bin, the Bin is set not to keep deleted files, or the file is too large for it.");
            }

            if (performed != S_OK || sink.Deleted is not { } deleted || deleted != S_OK)
            {
                return Trashed.Not($"Windows did not move it to the Recycle Bin (error 0x{(performed != S_OK ? performed : sink.Deleted ?? 0):X8}).");
            }

            if (sink.Recycled is null)
            {
                return Trashed.Not("Windows deleted it, but did not say it went to the Recycle Bin.");
            }

            return Trashed.To(sink.Recycled);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return Trashed.Not($"Windows could not be asked to recycle it: {ex.Message}");
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }

            if (operation is not null)
            {
                Marshal.ReleaseComObject(operation);
            }
        }
    }

    private static void Check(int hresult, string call)
    {
        if (hresult != S_OK)
        {
            throw new COMException($"{call} failed (0x{hresult:X8}).", hresult);
        }
    }

    /// <summary>
    /// The progress sink for the delete: it cancels a delete that would destroy the file, and takes
    /// note of how the delete went and where in the Recycle Bin the file went.
    /// </summary>
    [ComVisible(true)]
    private sealed class Sink : IFileOperationProgressSink
    {
        public bool WouldDestroy { get; private set; }

        public int? Deleted { get; private set; }

        public string? Recycled { get; private set; }

        public int PreDeleteItem(uint flags, IShellItem item)
        {
            if (RecycleBin.WouldDestroy(flags))
            {
                WouldDestroy = true;
                return E_ABORT; // an error from here cancels the delete
            }

            return S_OK;
        }

        public int PostDeleteItem(uint flags, IShellItem item, int hrDelete, IShellItem? newlyCreated)
        {
            Deleted = hrDelete;
            if (newlyCreated is not null && newlyCreated.GetDisplayName(SIGDN_FILESYSPATH, out IntPtr name) == S_OK)
            {
                Recycled = Marshal.PtrToStringUni(name);
                Marshal.FreeCoTaskMem(name);
            }

            return S_OK;
        }

        public int StartOperations() => S_OK;

        public int FinishOperations(int hrResult) => S_OK;

        public int PreRenameItem(uint flags, IShellItem item, string? newName) => S_OK;

        public int PostRenameItem(uint flags, IShellItem item, string newName, int hrRename, IShellItem? newlyCreated) => S_OK;

        public int PreMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName) => S_OK;

        public int PostMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName, int hrMove, IShellItem? newlyCreated) => S_OK;

        public int PreCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName) => S_OK;

        public int PostCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, string? newName, int hrCopy, IShellItem? newlyCreated) => S_OK;

        public int PreNewItem(uint flags, IShellItem destinationFolder, string? newName) => S_OK;

        public int PostNewItem(uint flags, IShellItem destinationFolder, string? newName, string? templateName, uint fileAttributes, int hrNew, IShellItem? newItem) => S_OK;

        public int UpdateProgress(uint workTotal, uint workSoFar) => S_OK;

        public int ResetTimer() => S_OK;

        public int PauseTimer() => S_OK;

        public int ResumeTimer() => S_OK;
    }

    private const uint SIGDN_FILESYSPATH = 0x80058000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHCreateItemFromParsingName(
        string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    // The interfaces as shobjidl.idl declares them, every method in its place, since a COM
    // interface is called by the place of each method in it; each answers its HRESULT itself.

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr result);

        [PreserveSig] int GetParent(out IShellItem parent);

        [PreserveSig] int GetDisplayName(uint sigdn, out IntPtr name);

        [PreserveSig] int GetAttributes(uint mask, out uint attributes);

        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();

        [PreserveSig] int FinishOperations(int hrResult);

        [PreserveSig] int PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig] int PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hrRename, IShellItem? newlyCreated);

        [PreserveSig] int PreMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig] int PostMoveItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrMove, IShellItem? newlyCreated);

        [PreserveSig] int PreCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig] int PostCopyItem(uint flags, IShellItem item, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, int hrCopy, IShellItem? newlyCreated);

        [PreserveSig] int PreDeleteItem(uint flags, IShellItem item);

        [PreserveSig] int PostDeleteItem(uint flags, IShellItem item, int hrDelete, IShellItem? newlyCreated);

        [PreserveSig] int PreNewItem(uint flags, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName);

        [PreserveSig] int PostNewItem(uint flags, IShellItem destinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? newName, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, uint fileAttributes, int hrNew, IShellItem? newItem);

        [PreserveSig] int UpdateProgress(uint workTotal, uint workSoFar);

        [PreserveSig] int ResetTimer();

        [PreserveSig] int PauseTimer();

        [PreserveSig] int ResumeTimer();
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink sink, out uint cookie);

        [PreserveSig] int Unadvise(uint cookie);

        [PreserveSig] int SetOperationFlags(uint flags);

        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);

        [PreserveSig] int SetProgressDialog(IntPtr dialog);

        [PreserveSig] int SetProperties(IntPtr array);

        [PreserveSig] int SetOwnerWindow(IntPtr owner);

        [PreserveSig] int ApplyPropertiesToItem(IShellItem item);

        [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);

        [PreserveSig] int RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string name, IFileOperationProgressSink? sink);

        [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items, [MarshalAs(UnmanagedType.LPWStr)] string name);

        [PreserveSig] int MoveItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);

        [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);

        [PreserveSig] int CopyItem(IShellItem item, IShellItem folder, [MarshalAs(UnmanagedType.LPWStr)] string? name, IFileOperationProgressSink? sink);

        [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items, IShellItem folder);

        [PreserveSig] int DeleteItem(IShellItem item, IFileOperationProgressSink? sink);

        [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);

        [PreserveSig] int NewItem(IShellItem folder, uint attributes, [MarshalAs(UnmanagedType.LPWStr)] string? name, [MarshalAs(UnmanagedType.LPWStr)] string? templateName, IFileOperationProgressSink? sink);

        [PreserveSig] int PerformOperations();

        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
