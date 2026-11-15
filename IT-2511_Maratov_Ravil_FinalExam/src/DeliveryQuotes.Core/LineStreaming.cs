namespace DeliveryQuotes.Core;

/// <summary>
/// Streams the lines of a text source while keeping the reader open only as long as the enumeration lasts.
/// OWNER: the iterator below owns the reader it opens (the "using" declaration).
/// RELEASE POINT: the reader is disposed when the enumeration finishes, when it fails with an exception,
/// and when the consumer stops early (foreach with break, Take, First: they dispose the enumerator, which runs the iterator's cleanup).
/// </summary>
public static class LineStreaming
{
    public static IEnumerable<string> ReadLines(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return ReadLines(() => new StreamReader(path));
    }

    /// <summary>The reader factory is called when enumeration starts, not when this method is called.</summary>
    public static IEnumerable<string> ReadLines(Func<TextReader> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        return Iterate(open);
    }

    private static IEnumerable<string> Iterate(Func<TextReader> open)
    {
        using var reader = open();
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
