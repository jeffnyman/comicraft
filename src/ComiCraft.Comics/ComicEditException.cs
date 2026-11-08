namespace ComiCraft.Comics;

/// <summary>
/// Why a comic could not be changed: a value that does not belong in its field, a ComicInfo.xml
/// that cannot be read and so is not to be written over, or an archive that cannot be written.
/// The message is meant for the reader.
/// </summary>
public sealed class ComicEditException(string message, Exception? inner = null) : Exception(message, inner);
