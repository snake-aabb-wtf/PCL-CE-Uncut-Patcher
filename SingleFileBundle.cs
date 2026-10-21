using System.Text;

namespace PclCeUncut.Patcher;

/// <summary>
/// .NET 单文件包（single-file bundle）的只读视图。
/// 包尾是条目表，每条为：offset(8) size(8) compressedSize(8) type(1) nameLength(1) name。
/// </summary>
internal sealed class SingleFileBundle
{
    public sealed record Entry(long Offset, long Size, long CompressedSize, byte Type, string Path)
    {
        public int Length => 26 + Encoding.ASCII.GetByteCount(Path);
    }

    private const int MaxTableScan = 4 * 1024 * 1024;

    public static SingleFileBundle Open(byte[] data)
    {
        if (data.Length < 64 || data[0] != (byte)'M' || data[1] != (byte)'Z')
            throw new PatcherException("不是 Windows PE 文件");

        int tableOffset = FindTable(data);
        if (tableOffset < 0)
            throw new PatcherException("找不到内嵌的 .NET 单文件包结构，这可能不是 PCL CE 的发布产物");

        var entries = ReadEntries(data, tableOffset);
        if (entries.Count == 0)
            throw new PatcherException("单文件包是空的");

        foreach (var entry in entries)
        {
            if (entry.Offset < 0 || entry.Size < 0 || entry.Offset + entry.Size > data.Length)
                throw new PatcherException("单文件包条目越界：" + entry.Path);
            if (entry.Offset < 64)
                throw new PatcherException("单文件包条目位置异常：" + entry.Path);
        }

        return new SingleFileBundle(data, tableOffset, entries);
    }

    private SingleFileBundle(byte[] data, int tableOffset, IReadOnlyList<Entry> entries)
    {
        Data = data;
        TableOffset = tableOffset;
        Entries = entries;
    }

    public byte[] Data { get; }

    public int TableOffset { get; }

    public IReadOnlyList<Entry> Entries { get; }

    public Entry? Find(string path)
    {
        foreach (var entry in Entries)
        {
            if (string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
                return entry;
        }
        return null;
    }

    public byte[] Read(Entry entry)
    {
        if (entry.CompressedSize != 0)
            throw new PatcherException("内嵌文件被压缩，暂不支持：" + entry.Path);
        var result = new byte[entry.Size];
        Array.Copy(Data, entry.Offset, result, 0, entry.Size);
        return result;
    }

    public void Write(Entry entry, byte[] content)
    {
        if (entry.CompressedSize != 0)
            throw new PatcherException("内嵌文件被压缩，暂不支持：" + entry.Path);
        if (content.Length != entry.Size)
            throw new PatcherException("替换内容长度与原始条目不一致：" + entry.Path);
        Array.Copy(content, 0, Data, entry.Offset, content.Length);
    }

    private static int FindTable(byte[] data)
    {
        int length = data.Length;
        int scanFrom = Math.Max(64, length - MaxTableScan);
        for (int pos = scanFrom; pos <= length - 26; pos++)
        {
            if (TryParseChain(data, pos, out int count) && count >= 3)
                return pos;
        }
        return -1;
    }

    private static bool TryParseChain(byte[] data, int start, out int count)
    {
        count = 0;
        int pos = start;
        while (pos < data.Length)
        {
            if (!TryReadEntry(data, pos, out var entry))
                return false;
            count++;
            pos += entry.Length;
        }
        return pos == data.Length;
    }

    private static List<Entry> ReadEntries(byte[] data, int start)
    {
        var list = new List<Entry>();
        int pos = start;
        while (pos < data.Length && TryReadEntry(data, pos, out var entry))
        {
            list.Add(entry);
            pos += entry.Length;
        }
        return list;
    }

    private static bool TryReadEntry(byte[] data, int pos, out Entry entry)
    {
        entry = null!;
        if (pos + 26 > data.Length)
            return false;

        long offset = BitConverter.ToInt64(data, pos);
        long size = BitConverter.ToInt64(data, pos + 8);
        long compressed = BitConverter.ToInt64(data, pos + 16);
        byte type = data[pos + 24];
        int nameLength = data[pos + 25];

        if (type is < 1 or > 5)
            return false;
        if (nameLength < 2 || nameLength > 260)
            return false;
        int nameStart = pos + 26;
        if (nameStart + nameLength > data.Length)
            return false;
        for (int i = 0; i < nameLength; i++)
        {
            byte c = data[nameStart + i];
            if (c < 32 || c > 126)
                return false;
        }

        if (offset < 0 || size < 0 || offset + size > data.Length)
            return false;
        if (compressed < 0 || compressed > size)
            return false;
        if (nameLength < 3)
            return false;

        var name = Encoding.ASCII.GetString(data, nameStart, nameLength);
        if (!name.Contains('.') && !name.Contains('/'))
            return false;

        entry = new Entry(offset, size, compressed, type, name);
        return true;
    }
}
