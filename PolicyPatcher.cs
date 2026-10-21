using Mono.Cecil;
using Mono.Cecil.Cil;

namespace PclCeUncut.Patcher;

internal enum PatchState
{
    Original,
    Patched
}

internal sealed record Inspection(PatchState State, Version Version);

/// <summary>
/// 改 PCL CE 的地区门禁：把 RestrictedFeaturePolicy 的三个入口替换成常量返回。
/// 采用原地改写方法体的方式（tiny 头 + IL），文件长度不变，单文件包无需重建。
/// </summary>
internal static class PolicyPatcher
{
    internal const string TypeName = "PCL.RestrictedFeaturePolicy";

    private const string CheckName = "Check";
    private const string IsRestrictedName = "get_IsRestricted";
    private const string ThrowName = "ThrowIfRestricted";

    private static readonly byte[] ReturnFalse = [0x16, 0x2A]; // ldc.i4.0; ret
    private static readonly byte[] ReturnTrue = [0x17, 0x2A]; // ldc.i4.1; ret
    private static readonly byte[] ReturnVoid = [0x2A]; // ret

    public static Inspection Inspect(byte[] dll)
    {
        using var stream = new MemoryStream(dll, writable: false);
        using var assembly = AssemblyDefinition.ReadAssembly(stream);
        var type = GetType(assembly);
        var isRestricted = GetMethod(type, IsRestrictedName, 0);
        var check = GetMethod(type, CheckName, 2);

        if (IsBody(check, OpCodes.Ldc_I4_1, OpCodes.Ret)
            && IsBody(isRestricted, OpCodes.Ldc_I4_0, OpCodes.Ret))
        {
            return new Inspection(PatchState.Patched, assembly.Name.Version);
        }

        if (!LooksLikeOriginal(check))
            throw new PatcherException("门禁方法的结构和预期不一致，上游可能改过实现，已停止");

        return new Inspection(PatchState.Original, assembly.Name.Version);
    }

    public static byte[] Patch(byte[] dll)
    {
        using var stream = new MemoryStream(dll, writable: false);
        using var assembly = AssemblyDefinition.ReadAssembly(stream);
        var type = GetType(assembly);
        var isRestricted = GetMethod(type, IsRestrictedName, 0);
        var check = GetMethod(type, CheckName, 2);
        var throwIf = GetMethod(type, ThrowName, 1);

        if (IsBody(check, OpCodes.Ldc_I4_1, OpCodes.Ret)
            && IsBody(isRestricted, OpCodes.Ldc_I4_0, OpCodes.Ret))
        {
            throw new PatcherException("这个文件已经打过补丁了");
        }

        if (!LooksLikeOriginal(check))
            throw new PatcherException("门禁方法的结构和预期不一致，上游可能改过实现，已停止");

        var patched = (byte[])dll.Clone();
        var pe = new PeImage(patched);
        WriteTinyBody(patched, pe, isRestricted.RVA, ReturnFalse);
        WriteTinyBody(patched, pe, check.RVA, ReturnTrue);
        WriteTinyBody(patched, pe, throwIf.RVA, ReturnVoid);

        Verify(patched);
        return patched;
    }

    private static void Verify(byte[] dll)
    {
        using var stream = new MemoryStream(dll, writable: false);
        using var assembly = AssemblyDefinition.ReadAssembly(stream);
        var type = GetType(assembly);

        if (!IsBody(GetMethod(type, IsRestrictedName, 0), OpCodes.Ldc_I4_0, OpCodes.Ret))
            throw new PatcherException("打补丁后校验失败：IsRestricted 不是恒 false");
        if (!IsBody(GetMethod(type, CheckName, 2), OpCodes.Ldc_I4_1, OpCodes.Ret))
            throw new PatcherException("打补丁后校验失败：Check 不是恒 true");
        if (!IsBody(GetMethod(type, ThrowName, 1), OpCodes.Ret))
            throw new PatcherException("打补丁后校验失败：ThrowIfRestricted 不是空实现");
    }

    private static void WriteTinyBody(byte[] data, PeImage pe, int rva, byte[] il)
    {
        if (rva == 0)
            throw new PatcherException("目标方法没有方法体");
        if (il.Length > 63)
            throw new PatcherException("替换后的方法体过长");

        int offset = pe.RvaToOffset(rva);
        data[offset] = (byte)((il.Length << 2) | 0x2); // tiny 头
        Array.Copy(il, 0, data, offset + 1, il.Length);
    }

    private static TypeDefinition GetType(AssemblyDefinition assembly)
    {
        return assembly.MainModule.GetType(TypeName)
               ?? throw new PatcherException($"找不到 {TypeName}，这可能不是 PCL CE 的主程序");
    }

    private static MethodDefinition GetMethod(TypeDefinition type, string name, int parameterCount)
    {
        foreach (var method in type.Methods)
        {
            if (method.Name == name && method.Parameters.Count == parameterCount)
                return method;
        }
        throw new PatcherException($"找不到方法 {TypeName}.{name}（{parameterCount} 个参数）");
    }

    private static bool IsBody(MethodDefinition method, params OpCode[] expected)
    {
        if (method.Body is null || method.Body.Instructions.Count != expected.Length)
            return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (method.Body.Instructions[i].OpCode != expected[i])
                return false;
        }
        return true;
    }

    private static bool LooksLikeOriginal(MethodDefinition check)
    {
        foreach (var instruction in check.Body.Instructions)
        {
            if (instruction.OpCode == OpCodes.Ldstr
                && instruction.Operand is string text
                && text.StartsWith("Region.Feature.", StringComparison.Ordinal))
            {
                return true;
            }

            if (instruction.OpCode == OpCodes.Call
                && instruction.Operand is MethodReference method
                && method.Name == "Assess")
            {
                return true;
            }
        }

        return false;
    }
}
