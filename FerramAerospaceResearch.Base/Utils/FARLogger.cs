using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using UnityEngine;

namespace FerramAerospaceResearch
{
    public static class FARLogger
    {
        public const string DefaultExceptionMessage = "Logged exception:";
        public static readonly FARLogHandler LogHandler = new FARLogHandler();
        public static readonly string Tag = $"[FAR {Version.ShortString}]";

        // force set log level on initialization by looking at files next to plugins
        private static readonly Regex logLevelRegex =
            new Regex(@"FARLogger_(\w+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        static FARLogger()
        {
            SetupLevel();
            LogHandler.LogHandler.LogFormat(LogType.Log, null, "{0}: Using {1} log level", Tag, Level);
        }

        public static LogLevel Level
        {
            get { return LogHandler.Level; }
            set { LogHandler.Level = value; }
        }

        public static bool IsEnabledFor(LogLevel level)
        {
            return LogHandler.EnabledFor(level);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Log(LogLevel level, object message)
        {
            LogHandler.Log(level, Tag, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(object message)
        {
            LogHandler.LogInfo(Tag, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(object message)
        {
            LogHandler.LogDebug(Tag, message);
        }

        [Conditional("LOG_TRACE"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(object message)
        {
            LogHandler.LogTrace(Tag, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(object message)
        {
            LogHandler.LogWarning(Tag, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(object message)
        {
            LogHandler.LogError(Tag, message);
        }

        [Conditional("ASSERT")]
        public static void Assert(
            bool condition,
            object message,
            [CallerMemberName]
            string memberName = "",
            [CallerFilePath]
            string filePath = "",
            [CallerLineNumber]
            int lineNumber = 0
        )
        {
            LogHandler.LogAssertFormat(condition,
                                       Tag,
                                       "{2}({3}): {1}: {0}",
                                       message,
                                       memberName,
                                       filePath,
                                       lineNumber.ToString());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Exception(Exception exception, object message = null)
        {
            LogHandler.LogException(exception, Tag, message ?? DefaultExceptionMessage);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogFormat(LogLevel level, string format, params object[] args)
        {
            LogHandler.LogFormat(level, Tag, format, args);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void InfoFormat(string format, params object[] args)
        {
            LogHandler.LogInfoFormat(Tag, format, args);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void DebugFormat(string format, params object[] args)
        {
            LogHandler.LogDebugFormat(Tag, format, args);
        }

        [Conditional("LOG_TRACE"), MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void TraceFormat(string format, params object[] args)
        {
            LogHandler.LogTraceFormat(Tag, format, args);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WarningFormat(string format, params object[] args)
        {
            LogHandler.LogWarningFormat(Tag, format, args);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ErrorFormat(string format, params object[] args)
        {
            LogHandler.LogErrorFormat(Tag, format, args);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), Conditional("ASSERT")]
        public static void AssertFormat(
            bool condition,
            string format,
            [CallerMemberName]
            string memberName = "",
            [CallerFilePath]
            string filePath = "",
            [CallerLineNumber]
            int lineNumber = 0,
            params object[] args
        )
        {
            // ReSharper disable ExplicitCallerInfoArgument
            Assert(condition, string.Format(format, args), memberName, filePath, lineNumber);
            // ReSharper restore ExplicitCallerInfoArgument
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining), Conditional("ASSERT")]
        public static void AssertFormat(bool condition, string format, SourceInfo source, params object[] args)
        {
            // ReSharper disable ExplicitCallerInfoArgument
            Assert(condition, string.Format(format, args), source.MemberName, source.FilePath, source.LineNumber);
            // ReSharper restore ExplicitCallerInfoArgument
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ExceptionFormat(Exception exception, string format, params object[] args)
        {
            LogHandler.LogExceptionFormat(exception, Tag, format, args);
        }

        private static void SetupLevel()
        {
            LogLevel level = LogLevel.Assert;

            bool exists = false;
            string[] files = Directory.GetFiles(PathUtil.PluginsDir);

            foreach (string file in files)
            {
                Match match = logLevelRegex.Match(Path.GetFileNameWithoutExtension(file));
                if (!match.Success)
                {
                    DebugFormat("{0} did not match the regex pattern {1}", file, logLevelRegex);
                    continue;
                }

                if (Enum.TryParse(match.Groups[1].Value, true, out LogLevel l))
                {
                    exists = true;
                    if (l < level)
                        level = l;
                    DebugFormat("Matched {0} to {1}", file, level);
                }
                else
                {
                    Warning($"Unknown level name {match.Value}");
                }
            }

            if (!exists)
                return;

            Level = level;
        }
    }
}
