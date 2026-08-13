using System.Collections;
using System.Text;
using Bayan.Application.Common.Interfaces;
using Bayan.Application.Common.Models;

namespace Bayan.Infrastructure.Caching;

/// <summary>
/// A large cached result spilled to local disk: rows live in an NDJSON data file
/// (one <see cref="ResultRowSerializer"/> line each) with a sidecar index of per-row byte offsets
/// for O(1) seeking. Only the columns, total-row count and file handles stay in the heap. Paging
/// with no filter/sort seeks directly via the index; filtering/sorting stream the file once and
/// keep at most the page plus (for sort) small (key, offset) pairs in memory.
/// </summary>
internal sealed class DiskCachedResult : ICachedResult
{
    private readonly string _dataPath;
    private readonly string _indexPath;

    public DiskCachedResult(
        string dataPath,
        string indexPath,
        IReadOnlyList<string> columns,
        int totalRows,
        long diskSizeBytes)
    {
        _dataPath = dataPath;
        _indexPath = indexPath;
        Columns = columns;
        TotalRows = totalRows;
        ApproxSizeBytes = diskSizeBytes;
    }

    public IReadOnlyList<string> Columns { get; }
    public int TotalRows { get; }
    public long ApproxSizeBytes { get; }
    public bool IsOnDisk => true;

    public CachedResultPage GetPage(
        int pageIndex,
        int pageSize,
        string? sortColumn,
        string? sortDir,
        IReadOnlyDictionary<string, string> filters)
    {
        if (pageSize <= 0) pageSize = 25;
        if (pageIndex < 0) pageIndex = 0;

        var hasFilters = filters.Any(kv => !string.IsNullOrEmpty(kv.Value));
        var hasSort = !string.IsNullOrWhiteSpace(sortColumn) && Columns.Contains(sortColumn);

        if (!hasFilters && !hasSort)
            return FastPage(pageIndex, pageSize);
        if (!hasSort)
            return FilterPage(pageIndex, pageSize, filters);
        return SortPage(pageIndex, pageSize, sortColumn!, sortDir, filters, hasFilters);
    }

    // No filter/sort: seek straight to the page via the offset index.
    private CachedResultPage FastPage(int pageIndex, int pageSize)
    {
        var start = Math.Min((long)pageIndex * pageSize, TotalRows);
        var count = (int)Math.Min(pageSize, TotalRows - start);
        var rows = ReadRowsRange((int)start, count);
        return new CachedResultPage(rows, TotalRows, TotalRows);
    }

    // Filter, no sort: one streaming pass; count all matches, keep only the page window.
    private CachedResultPage FilterPage(
        int pageIndex, int pageSize, IReadOnlyDictionary<string, string> filters)
    {
        var skip = (long)pageIndex * pageSize;
        var page = new List<IReadOnlyDictionary<string, object?>>(pageSize);
        var matched = 0;
        foreach (var (_, line) in ReadLines())
        {
            var row = ResultRowSerializer.DecodeRow(line, Columns);
            if (!CachedRowOps.Matches(row, filters))
                continue;
            if (matched >= skip && page.Count < pageSize)
                page.Add(row);
            matched++;
        }
        return new CachedResultPage(page, matched, TotalRows);
    }

    // Sort (optionally filtered): stream to collect (sort-key, offset) for matches, order those,
    // then seek to just the page's rows. Full rows are never all held at once.
    private CachedResultPage SortPage(
        int pageIndex, int pageSize, string sortColumn, string? sortDir,
        IReadOnlyDictionary<string, string> filters, bool hasFilters)
    {
        var keyed = new List<(object? Key, long Offset)>();
        foreach (var (offset, line) in ReadLines())
        {
            var row = ResultRowSerializer.DecodeRow(line, Columns);
            if (hasFilters && !CachedRowOps.Matches(row, filters))
                continue;
            keyed.Add((row.GetValueOrDefault(sortColumn), offset));
        }

        var filteredTotal = keyed.Count;
        var dir = CachedRowOps.Direction(sortDir);
        keyed.Sort((a, b) =>
        {
            var c = CachedRowOps.Compare(a.Key, b.Key) * dir;
            return c != 0 ? c : a.Offset.CompareTo(b.Offset);
        });

        var start = Math.Min((long)pageIndex * pageSize, filteredTotal);
        var count = (int)Math.Min(pageSize, filteredTotal - start);

        var rows = new List<IReadOnlyDictionary<string, object?>>(count);
        using var fs = OpenData();
        foreach (var (_, offset) in keyed.Skip((int)start).Take(count))
            rows.Add(ReadRowAt(fs, offset));

        return new CachedResultPage(rows, filteredTotal, TotalRows);
    }

    public IReadOnlyList<IReadOnlyDictionary<string, object?>> AsRowList() => new DiskRowList(this);

    public void Dispose()
    {
        TryDelete(_dataPath);
        TryDelete(_indexPath);
    }

    // ---------- file access ----------

    private FileStream OpenData() =>
        new(_dataPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);

    private long ReadOffset(int rowIndex)
    {
        using var idx = new FileStream(_indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        idx.Seek((long)rowIndex * sizeof(long), SeekOrigin.Begin);
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        idx.ReadExactly(buffer);
        return BitConverter.ToInt64(buffer);
    }

    private List<IReadOnlyDictionary<string, object?>> ReadRowsRange(int start, int count)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>(Math.Max(0, count));
        if (count <= 0)
            return rows;

        var startByte = ReadOffset(start);
        var endByte = start + count < TotalRows ? ReadOffset(start + count) : new FileInfo(_dataPath).Length;

        using var fs = OpenData();
        fs.Seek(startByte, SeekOrigin.Begin);
        var buffer = new byte[endByte - startByte];
        fs.ReadExactly(buffer);

        var lineStart = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != (byte)'\n')
                continue;
            rows.Add(ResultRowSerializer.DecodeRow(
                Encoding.UTF8.GetString(buffer, lineStart, i - lineStart), Columns));
            lineStart = i + 1;
        }
        return rows;
    }

    private Dictionary<string, object?> ReadRowAt(FileStream fs, long offset)
    {
        fs.Seek(offset, SeekOrigin.Begin);
        var bytes = new List<byte>(256);
        int b;
        while ((b = fs.ReadByte()) != -1 && b != '\n')
            bytes.Add((byte)b);
        return ResultRowSerializer.DecodeRow(Encoding.UTF8.GetString(bytes.ToArray()), Columns);
    }

    /// <summary>Streams the data file yielding each row's start byte offset and its raw JSON line.</summary>
    private IEnumerable<(long Offset, string Line)> ReadLines()
    {
        using var fs = OpenData();
        var buffer = new byte[1 << 16];
        var lineBuffer = new List<byte>(256);
        long lineStart = 0;
        long pos = 0;
        int read;
        while ((read = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                if (buffer[i] == (byte)'\n')
                {
                    yield return (lineStart, Encoding.UTF8.GetString(lineBuffer.ToArray()));
                    lineBuffer.Clear();
                    lineStart = pos + 1;
                }
                else
                {
                    lineBuffer.Add(buffer[i]);
                }
                pos++;
            }
        }
        if (lineBuffer.Count > 0)
            yield return (lineStart, Encoding.UTF8.GetString(lineBuffer.ToArray()));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort: a concurrent reader may still hold the handle; startup cleanup will retry.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Lazy, re-enumerable list view over the spilled rows for the file exporters. <see cref="Count"/>
    /// is O(1); enumeration streams from disk (a fresh handle per pass) so exports never re-materialize
    /// the full result into the heap. The indexer seeks via the offset index.
    /// </summary>
    private sealed class DiskRowList : IReadOnlyList<IReadOnlyDictionary<string, object?>>
    {
        private readonly DiskCachedResult _owner;

        public DiskRowList(DiskCachedResult owner) => _owner = owner;

        public int Count => _owner.TotalRows;

        public IReadOnlyDictionary<string, object?> this[int index]
        {
            get
            {
                using var fs = _owner.OpenData();
                return _owner.ReadRowAt(fs, _owner.ReadOffset(index));
            }
        }

        public IEnumerator<IReadOnlyDictionary<string, object?>> GetEnumerator()
        {
            foreach (var (_, line) in _owner.ReadLines())
                yield return ResultRowSerializer.DecodeRow(line, _owner.Columns);
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
