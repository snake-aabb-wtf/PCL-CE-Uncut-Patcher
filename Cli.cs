using System.Text;

namespace PclCeUncut.Patcher;

internal static class Cli
{
    internal static int Run(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
        }
        catch
        {
            // 控制台不支持 UTF-8 时保持默认编码
        }

        string? input = null;
        string? output = null;
        bool checkOnly = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help":
                    Usage();
                    return 0;
                case "-v" or "--version":
                    Console.WriteLine("PCL-CE-Uncut-Patcher " + Program.Version);
                    return 0;
                case "--check":
                    checkOnly = true;
                    break;
                case "-o" or "--output":
                    if (i + 1 >= args.Length)
                    {
                        Error("缺少 -o 的参数");
                        return 2;
                    }
                    output = Path.GetFullPath(args[++i]);
                    break;
                default:
                    if (args[i].StartsWith('-'))
                    {
                        Error("未知参数：" + args[i]);
                        return 2;
                    }
                    if (input is not null)
                    {
                        Error("多余的参数：" + args[i]);
                        return 2;
                    }
                    input = Path.GetFullPath(args[i]);
                    break;
            }
        }

        if (input is null)
        {
            Usage();
            return 2;
        }
        if (!File.Exists(input))
        {
            Error("文件不存在：" + input);
            return 1;
        }

        try
        {
            if (checkOnly)
            {
                var info = Patcher.Inspect(input);
                PrintInspection(input, info);
                Console.WriteLine("检查通过（未写出文件）");
                return 0;
            }

            string target = output ?? Patcher.DefaultOutput(input);
            Patcher.Patch(input, target, Console.WriteLine);
            return 0;
        }
        catch (PatcherException e)
        {
            Error(e.Message);
            return 1;
        }
        catch (Exception e)
        {
            Error(e.GetType().Name + "：" + e.Message);
            return 1;
        }
    }

    internal static void PrintInspection(string path, InspectionInfo info)
    {
        Console.WriteLine($"读取：{Path.GetFileName(path)}（{info.Size:N0} 字节）");
        Console.WriteLine($"格式：.NET 单文件包（{info.EntryCount} 个内嵌文件）");
        Console.WriteLine("PCL CE 版本：" + info.Version);
        if (info.State == PatchState.Patched)
            throw new PatcherException("这个文件已经打过补丁了");
        Console.WriteLine("门禁：PCL.RestrictedFeaturePolicy（可恢复）");
    }

    private static void Error(string message)
    {
        Console.Error.WriteLine("× " + message);
    }

    private static void Usage()
    {
        Console.WriteLine($"""
            PCL-CE-Uncut-Patcher {Program.Version}

            用法：
              PCL-CE-Uncut-Patcher <输入文件> [-o 输出文件] [--check]
              PCL-CE-Uncut-Patcher --gui [输入文件]

            不带参数启动是图形界面；带参数是命令行，两者行为一致。

            支持：
              PCL2_CE_Release_x64.exe      PCL CE 官方 x64 单文件包
              PCL2_CE_Release_ARM64.exe    PCL CE 官方 ARM64 单文件包

            说明：
              只修改内嵌的 Plain Craft Launcher 2.dll 里的 PCL.RestrictedFeaturePolicy，
              把中国大陆地区的下载/安装限制改成恒放行；文件长度保持不变，其余内容原样保留。
              输出默认加 -uncut 后缀，原文件不会被修改。
            """);
    }
}
