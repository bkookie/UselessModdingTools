using System.Runtime.CompilerServices;

namespace UselessModdingTools;

internal static class Log
{
    internal static string ErrorMsg(string msg, [CallerFilePath] string filepath = "", [CallerLineNumber] int line = 0)
    {
        string file = Path.GetFileName(filepath);
        return $"<{file} line:{line}> {msg}";
    }
}

internal static class LogExtensions
{
    extension(string msg)
    {
        internal string AsErrorMsg([CallerFilePath] string filepath = "", [CallerLineNumber] int line = 0) => Log.ErrorMsg(msg, filepath, line);
    }
}
