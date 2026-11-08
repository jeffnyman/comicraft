using System.IO.Compression;
using System.Text;

namespace ComiCraft.Comics.Tests;

/// <summary>Small comics made in memory, so the tests need no comic files.</summary>
internal static class TestComics
{
    /// <summary>A zip with the given entries, each holding its own name as its bytes.</summary>
    public static MemoryStream Cbz(params string[] entries) => Cbz(entries.Select(name => (name, name)).ToArray());

    /// <summary>A zip with the given entries and their text.</summary>
    public static MemoryStream Cbz(params (string Name, string Text)[] entries)
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// A RAR (version 4) archive of the given entries, stored, not compressed. Nothing free can
    /// write RAR, but its format is published, and an archive of stored files is simple: a
    /// signature, then blocks (the archive's, one for each file followed by the file, and one to
    /// end), each starting with the low 16 bits of the CRC-32 of the rest of its header.
    /// </summary>
    public static byte[] Rar(params (string Name, string Text)[] entries)
    {
        var rar = new MemoryStream();
        rar.Write("Rar!\u001A\u0007\0"u8);
        Block(rar, 0x73, 0, new byte[6]); // the archive: nothing but its reserved fields

        // January 1, 2020, as MS-DOS wrote a date and time
        const uint Time = (40u << 25) | (1u << 21) | (1u << 16);
        foreach (var (name, text) in entries)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            byte[] fileName = Encoding.UTF8.GetBytes(name);
            var header = new MemoryStream();
            var write = new BinaryWriter(header);
            write.Write((uint)data.Length);   // its size, packed
            write.Write((uint)data.Length);   // and unpacked, the same: it is stored
            write.Write((byte)3);             // made on Unix, so its folders are separated by /
            write.Write(Crc32(data));
            write.Write(Time);
            write.Write((byte)20);            // version 2.0 is needed to unpack it
            write.Write((byte)0x30);          // stored
            write.Write((ushort)fileName.Length);
            write.Write(0x81A4u);             // a plain file, rw-r--r--
            write.Write(fileName);
            Block(rar, 0x74, 0x8000, header.ToArray()); // 0x8000: its data follows the header
            rar.Write(data);
        }

        Block(rar, 0x7B, 0x4000, []);
        return rar.ToArray();
    }

    /// <summary>Writes a RAR with the given entries to a file, and returns its path.</summary>
    public static string CbrFile(string folder, string name, params (string Name, string Text)[] entries)
    {
        string path = Path.Combine(folder, name);
        File.WriteAllBytes(path, Rar(entries));
        return path;
    }

    private static void Block(Stream rar, byte type, ushort flags, byte[] body)
    {
        var header = new MemoryStream();
        var write = new BinaryWriter(header);
        write.Write(type);
        write.Write(flags);
        write.Write((ushort)(7 + body.Length));
        write.Write(body);
        byte[] bytes = header.ToArray();
        rar.Write(BitConverter.GetBytes((ushort)(Crc32(bytes) & 0xFFFF)));
        rar.Write(bytes);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>Writes a zip with the given entries and their text to a file, and returns its path.</summary>
    public static string CbzFile(string folder, string name, params (string Name, string Text)[] entries)
    {
        string path = Path.Combine(folder, name);
        using var file = File.Create(path);
        Cbz(entries).CopyTo(file);
        return path;
    }

    /// <summary>A comic of the given pages with the given ComicInfo.xml.</summary>
    public static ComicBook WithInfo(string comicInfo, params string[] pages)
    {
        using var cbz = Cbz([.. pages.Select(p => (p, p)), ("ComicInfo.xml", comicInfo)]);
        return ComicBook.Open(cbz, "Test Comic");
    }
}
