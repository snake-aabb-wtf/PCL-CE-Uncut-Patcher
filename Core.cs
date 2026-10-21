using System.Security.Cryptography;

namespace PclCeUncut.Patcher;

internal sealed record InspectionInfo(PatchState State, Version Version, int EntryCount, long Size);

internal sealed record PatchResult(long Size, string Sha256, Version Version, int EntryCount);

internal static class Patcher
{
    internal const string MainAssembly = "Plain Craft Launcher 2.dll";

    /// <summary>只看不动：读文件、解包、检查门禁方法，返回状态。</summary>
    internal static InspectionInfo Inspect(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        var bundle = SingleFileBundle.Open(data);
        var entry = bundle.Find(MainAssembly)
                    ?? throw new PatcherException($"单文件包里找不到 {MainAssembly}，这可能不是 PCL CE 的发布产物");
        var inspection = PolicyPatcher.Inspect(bundle.Read(entry));
        return new InspectionInfo(inspection.State, inspection.Version, bundle.Entries.Count, data.Length);
    }

    /// <summary>原地改内嵌主程序的方法体，写出新文件；原文件不动。</summary>
    internal static PatchResult Patch(string inputPath, string outputPath, Action<string> log)
    {
        string fullInput = Path.GetFullPath(inputPath);
        string fullOutput = Path.GetFullPath(outputPath);
        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
            throw new PatcherException("输出文件不能和输入文件相同");

        byte[] data = File.ReadAllBytes(fullInput);
        log($"读取：{Path.GetFileName(fullInput)}（{data.Length:N0} 字节）");

        var bundle = SingleFileBundle.Open(data);
        log($"格式：.NET 单文件包（{bundle.Entries.Count} 个内嵌文件）");

        var entry = bundle.Find(MainAssembly)
                    ?? throw new PatcherException($"单文件包里找不到 {MainAssembly}，这可能不是 PCL CE 的发布产物");
        log($"主程序：{entry.Path}（偏移 {entry.Offset:N0}，{entry.Size:N0} 字节）");

        byte[] mainAssembly = bundle.Read(entry);
        var inspection = PolicyPatcher.Inspect(mainAssembly);
        log("PCL CE 版本：" + inspection.Version);

        if (inspection.State == PatchState.Patched)
            throw new PatcherException("这个文件已经打过补丁了");

        log("门禁：PCL.RestrictedFeaturePolicy（可恢复）");

        byte[] patched = PolicyPatcher.Patch(mainAssembly);
        bundle.Write(entry, patched);

        // 从改完的字节里重新解析一次，确认包结构和补丁都对
        var verifyBundle = SingleFileBundle.Open(bundle.Data);
        var verifyEntry = verifyBundle.Find(MainAssembly)
                          ?? throw new PatcherException("回读校验失败：找不到主程序");
        if (PolicyPatcher.Inspect(verifyBundle.Read(verifyEntry)).State != PatchState.Patched)
            throw new PatcherException("回读校验失败：补丁没有生效");

        string? directory = Path.GetDirectoryName(fullOutput);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllBytes(fullOutput, bundle.Data);

        string sha256 = Convert.ToHexString(SHA256.HashData(bundle.Data)).ToLowerInvariant();
        log("输出：" + fullOutput);
        log($"大小：{bundle.Data.Length:N0} 字节（与输入相同：{(bundle.Data.Length == data.Length ? "是" : "否")}）");
        log("SHA-256：" + sha256);
        return new PatchResult(bundle.Data.Length, sha256, inspection.Version, bundle.Entries.Count);
    }

    internal static string DefaultOutput(string inputPath)
    {
        string fullInput = Path.GetFullPath(inputPath);
        string directory = Path.GetDirectoryName(fullInput) ?? ".";
        string name = Path.GetFileNameWithoutExtension(fullInput);
        string extension = Path.GetExtension(fullInput);
        return Path.Combine(directory, name + "-uncut" + extension);
    }
}
