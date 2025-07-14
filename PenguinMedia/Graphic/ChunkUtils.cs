namespace PenguinMedia.Graphic;

public static class ChunkUtils
{
    public static Span<(int, int)> LocateDdsChunks(byte[] data)
    {
        return LocateChunks(data, "DDS "u8.ToArray(), "POF0"u8.ToArray());
    }

    public static Span<(int, int)> LocateChunks(byte[] data, ReadOnlySpan<byte> header, ReadOnlySpan<byte> stopSign)
    {
        var chunks = new List<(int, int)>(2);
        var currentPos = 0;

        while (true)
        {
            var start = FindChunks(data, header, currentPos);
            if (start == -1) break;

            var stopPos = FindChunks(data, stopSign, start + header.Length);
            var nextHeader = FindChunks(data, header, start + header.Length);

            var hasStop = stopPos != -1;
            var hasNext = nextHeader != -1;

            if (hasStop && hasNext)
            {
                var end = Math.Min(stopPos, nextHeader);
                chunks.Add((start, end));
                currentPos = end;
            }
            else if (hasStop && !hasNext)
            {
                chunks.Add((start, stopPos));
                currentPos = stopPos;
            }
            else if (!hasStop && hasNext)
            {
                chunks.Add((start, nextHeader));
                currentPos = nextHeader;
            }
            else
            {
                chunks.Add((start, data.Length));
                break;
            }
        }

        return chunks.ToArray();
    }
    
    public static void ExtractChunks(byte[] data, string dstFolder, string baseName, string extension, Span<(int, int)> chunks)
    {
        Directory.CreateDirectory(dstFolder);

        for (var i = 0; i < chunks.Length; i++)
        {
            var (start, end) = chunks[i];
            var fileName = $"{baseName}_{(i + 1):D4}{extension}";
            var path = Path.Combine(dstFolder, fileName);

            using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write);
            fileStream.Write(data, start, end - start);
        }
    }
    
    public static void ReplaceChunks(byte[] data, string dstPath, ReadOnlySpan<(int, int)> chunks, byte[]?[] replacements)
    {
        if (replacements.Length < chunks.Length)
        {
            throw new ArgumentException($"Replacements length ({replacements.Length}) must be at least equal to chunks length ({chunks.Length}).");
        }

        var directory = Path.GetDirectoryName(dstPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var file = new FileStream(dstPath, FileMode.Create, FileAccess.Write);
        var cursor = 0;
        for (var i = 0; i < chunks.Length; i++)
        {
            var (start, end) = chunks[i];

            file.Write(data, cursor, start - cursor);
            
            if (replacements[i] is not {} replacement)
            {
                file.Write(data, start, end - start);
            }
            else
            {
                file.Write(replacement, 0, replacement.Length);
            }

            cursor = end;
        }
        
        if (cursor < data.Length)
        {
            file.Write(data, cursor, data.Length - cursor);
        }
    }
    
    private static int FindChunks(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle, int start)
    {
        if (needle.Length == 0)
        {
            return -1;
        }
        if (haystack.Length == 0 || start >= haystack.Length)
        {
            return -1;
        }
        
        for (var i = start; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] == needle[j]) continue;
                match = false;
                break;
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}