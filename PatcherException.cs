namespace PclCeUncut.Patcher;

/// <summary>工具预期的失败：给用户看的中文说明。</summary>
internal sealed class PatcherException : Exception
{
    public PatcherException(string message) : base(message)
    {
    }
}
