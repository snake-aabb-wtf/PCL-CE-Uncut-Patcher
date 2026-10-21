namespace PclCeUncut.Patcher;

/// <summary>最小的 PE 映像映射：只做 RVA → 文件偏移。</summary>
internal sealed class PeImage
{
    private readonly Section[] _sections;

    private readonly record struct Section(uint VirtualAddress, uint VirtualSize, uint RawPointer, uint RawSize);

    public PeImage(byte[] data)
    {
        if (data.Length < 0x40)
            throw new PatcherException("PE 文件太小");

        int peOffset = BitConverter.ToInt32(data, 0x3C);
        if (peOffset <= 0 || peOffset + 24 > data.Length)
            throw new PatcherException("PE 头位置异常");
        if (data[peOffset] != (byte)'P' || data[peOffset + 1] != (byte)'E'
            || data[peOffset + 2] != 0 || data[peOffset + 3] != 0)
            throw new PatcherException("不是有效的 PE 文件");

        int coff = peOffset + 4;
        int sectionCount = BitConverter.ToUInt16(data, coff + 2);
        int optionalSize = BitConverter.ToUInt16(data, coff + 16);
        int optional = coff + 20;
        int sectionStart = optional + optionalSize;
        if (sectionCount <= 0 || sectionCount > 96 || sectionStart + sectionCount * 40 > data.Length)
            throw new PatcherException("PE 节表异常");

        var sections = new Section[sectionCount];
        for (int i = 0; i < sectionCount; i++)
        {
            int at = sectionStart + i * 40;
            uint virtualSize = BitConverter.ToUInt32(data, at + 8);
            uint virtualAddress = BitConverter.ToUInt32(data, at + 12);
            uint rawSize = BitConverter.ToUInt32(data, at + 16);
            uint rawPointer = BitConverter.ToUInt32(data, at + 20);
            sections[i] = new Section(virtualAddress, virtualSize, rawPointer, rawSize);
        }

        _sections = sections;
    }

    public int RvaToOffset(int rva)
    {
        foreach (var section in _sections)
        {
            uint span = Math.Max(section.VirtualSize, section.RawSize);
            if ((uint)rva >= section.VirtualAddress && (uint)rva < section.VirtualAddress + span)
            {
                long offset = (long)section.RawPointer + ((uint)rva - section.VirtualAddress);
                if (offset < 0 || offset > int.MaxValue)
                    break;
                return (int)offset;
            }
        }
        throw new PatcherException($"无法把 RVA 0x{rva:X} 映射到文件偏移");
    }
}
