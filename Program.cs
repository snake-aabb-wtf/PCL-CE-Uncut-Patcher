using System.Runtime.InteropServices;

namespace PclCeUncut.Patcher;

internal static class Program
{
    internal const string Version = "1.0.0";

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            RunGui(null);
            return 0;
        }

        if (args[0] is "-g" or "--gui")
        {
            RunGui(args.Length > 1 ? args[1] : null);
            return 0;
        }

        return Cli.Run(args);
    }

    private static void RunGui(string? initialFile)
    {
        // 控制台子系统双击时会开一个黑窗口，进界面之前先脱离，免得它挂在任务栏上。
        // 从终端启动时 FreeConsole 只是解除关联，不会影响启动它的那个终端。
        FreeConsole();

        ApplicationConfiguration.Initialize();
        Application.Run(new Gui(initialFile));
    }

    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
}
