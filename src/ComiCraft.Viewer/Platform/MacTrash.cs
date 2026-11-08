using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ComiCraft.Viewer.Platform;

/// <summary>
/// The Trash on macOS, by Foundation's NSFileManager (trashItemAtURL:resultingItemURL:error:,
/// macOS 10.8 and later), as Finder itself moves files there: to the Trash of the volume the file
/// is on, and back again with Put Back. It is reached through the Objective-C runtime, which is
/// called as C; each objc_msgSend is declared with the exact arguments and result of the message it
/// sends, as it must be on Apple silicon, where a message's arguments are not passed as C's
/// variadic ones are.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class MacTrash : ITrash
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    public bool IsAvailable => true;

    public Trashed Move(string path)
    {
        // NSFileManager is in Foundation, which is loaded for its classes to be found
        NativeLibrary.Load(Foundation);

        // what Foundation hands back is let go of when the pool is, at the end
        void* pool = objc_autoreleasePoolPush();
        try
        {
            void* manager = Send(Class("NSFileManager"), Selector("defaultManager"));
            void* url = Send(Class("NSURL"), Selector("fileURLWithPath:"), String(Path.GetFullPath(path)));

            void* into = null;
            void* error = null;
            bool moved = SendTrash(manager, Selector("trashItemAtURL:resultingItemURL:error:"), url, &into, &error) != 0;
            if (moved)
            {
                return Trashed.To(into is null ? null : Text(Send(into, Selector("path"))));
            }

            return Trashed.Not(error is null ? "macOS did not say why." : Text(Send(error, Selector("localizedDescription"))) ?? "macOS did not say why.");
        }
        finally
        {
            objc_autoreleasePoolPop(pool);
        }
    }

    private static void* Class(string name) => objc_getClass(name) is var found && found is not null
        ? found
        : throw new InvalidOperationException($"The Objective-C class {name} was not found.");

    private static void* Selector(string name) => sel_registerName(name);

    /// <summary>An NSString of the text.</summary>
    private static void* String(string text)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(text + '\0');
        fixed (byte* bytes = utf8)
        {
            return Send(Class("NSString"), Selector("stringWithUTF8String:"), bytes);
        }
    }

    /// <summary>The text of an NSString; null for none.</summary>
    private static string? Text(void* nsString)
    {
        if (nsString is null)
        {
            return null;
        }

        byte* utf8 = (byte*)Send(nsString, Selector("UTF8String"));
        return utf8 is null ? null : Marshal.PtrToStringUTF8((IntPtr)utf8);
    }

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void* objc_getClass(string name);

    [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void* sel_registerName(string name);

    [LibraryImport(ObjC)]
    private static partial void* objc_autoreleasePoolPush();

    [LibraryImport(ObjC)]
    private static partial void objc_autoreleasePoolPop(void* pool);

    /// <summary>A message with no arguments that answers an object: [receiver selector].</summary>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void* Send(void* receiver, void* selector);

    /// <summary>A message with one argument, a pointer, that answers an object.</summary>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial void* Send(void* receiver, void* selector, void* argument);

    /// <summary>trashItemAtURL:resultingItemURL:error:, which answers a BOOL, a signed char.</summary>
    [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
    private static partial sbyte SendTrash(void* receiver, void* selector, void* url, void** resultingUrl, void** error);
}
