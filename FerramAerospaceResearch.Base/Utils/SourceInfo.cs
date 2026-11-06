using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace FerramAerospaceResearch
{
    public readonly struct SourceInfo
    {
        public readonly string MemberName;
        public readonly string FilePath;
        public readonly int LineNumber;

        private SourceInfo(string memberName, string filePath, int lineNumber)
        {
            MemberName = memberName;
            FilePath = filePath;
            LineNumber = lineNumber;
        }

        public static SourceInfo Current(
            [CallerMemberName]
            string memberName = "",
            [CallerFilePath]
            string filePath = "",
            [CallerLineNumber]
            int lineNumber = 0
        )
        {
            return new SourceInfo(memberName, filePath, lineNumber);
        }

        public static SourceInfo Current(int depth)
        {
            // frame 0 - Current
            // frame 1 - caller method
            var frame = new StackFrame(depth + 1, true);
            string method = frame.GetMethod().Name;
            // release mode doesn't have debug symbols
#if DEBUG
            string fileName = frame.GetFileName();
            int lineNumber = frame.GetFileLineNumber();

            return new SourceInfo(method, fileName, lineNumber);
#else
      return new SourceInfo(method, "<unknown>", -1);
#endif
        }

        public override string ToString()
        {
#if DEBUG
            return $"{FilePath}:{LineNumber.ToString()} - {MemberName}";
#else
      return MemberName;
#endif
        }
    }
}
