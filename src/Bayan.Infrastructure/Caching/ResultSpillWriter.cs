using System.Text;
using Bayan.Application.Common.Interfaces;

namespace Bayan.Infrastructure.Caching;

/// <summary>
/// Builds an <see cref="ICachedResult"/> for a finished read result, deciding memory vs disk: a
/// quick size estimate keeps clearly-small results in the heap without serializing; larger ones are
/// streamed out and, the moment the running size crosses <c>SpillThresholdBytes</c>, flushed to an
/// NDJSON data file plus an offset index (so at most ~one threshold's worth is buffered in memory
/// while writing). On any I/O failure the temp files are cleaned up and the exception rethrown, so
/// the caller can fall back to keeping the result in memory.
/// </summary>
internal static class ResultSpillWriter
{
    private const int EstimatedBytesPerCell = 32;

    public static ICachedResult Create(
        Guid jobId,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ResultCacheOptions options)
    {
        var estimate = (long)rows.Count * Math.Max(1, columns.Count) * EstimatedBytesPerCell;
        if (estimate < options.SpillThresholdBytes)
            return new InMemoryCachedResult(columns, rows, estimate);

        return WriteWithSpill(jobId, columns, rows, options);
    }

    private static ICachedResult WriteWithSpill(
        Guid jobId,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        ResultCacheOptions options)
    {
        var offsets = new List<long>(rows.Count);
        var buffer = new MemoryStream();
        FileStream? file = null;
        string? dataPath = null;
        string? indexPath = null;
        long pos = 0;

        try
        {
            foreach (var row in rows)
            {
                offsets.Add(pos);
                var bytes = Encoding.UTF8.GetBytes(ResultRowSerializer.EncodeRow(row, columns));

                Stream target = file ?? (Stream)buffer;
                target.Write(bytes, 0, bytes.Length);
                target.WriteByte((byte)'\n');
                pos += bytes.Length + 1;

                if (file is null && pos > options.SpillThresholdBytes)
                {
                    Directory.CreateDirectory(options.SpillDirectory);
                    dataPath = Path.Combine(options.SpillDirectory, $"{jobId:N}.ndjson");
                    file = new FileStream(dataPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                    buffer.Position = 0;
                    buffer.CopyTo(file);
                }
            }

            if (file is null)
            {
                // The estimate over-shot but the real size stayed under the threshold — keep it in memory.
                return new InMemoryCachedResult(columns, rows, pos);
            }

            file.Flush();
            file.Dispose();
            file = null;

            indexPath = Path.Combine(options.SpillDirectory, $"{jobId:N}.idx");
            using (var idx = new FileStream(indexPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(idx))
            {
                foreach (var offset in offsets)
                    writer.Write(offset);
            }

            return new DiskCachedResult(dataPath!, indexPath, columns, rows.Count, pos);
        }
        catch
        {
            file?.Dispose();
            SafeDelete(dataPath);
            SafeDelete(indexPath);
            throw;
        }
        finally
        {
            buffer.Dispose();
        }
    }

    private static void SafeDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
