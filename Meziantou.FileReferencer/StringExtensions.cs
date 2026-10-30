using System.Buffers;
using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Meziantou.FileReferencer;

internal static partial class StringExtensions
{
    public static LineSplitEnumerator SplitLines(this string str) => new(str);

    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public struct LineSplitEnumerator
    {
        private static SearchValues<char> NewLineCharacters { get; } = SearchValues.Create(['\r', '\n']);

        private readonly string _str;
        private int _offset;

        public LineSplitEnumerator(string str)
        {
            _str = str;
            _offset = 0;
            Current = default;
        }

        public readonly LineSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_offset >= _str.Length)
                return false;

            var str = _str;
            var index = str.AsSpan(_offset).IndexOfAny(NewLineCharacters);
            if (index == -1)
            {
                Current = new LineSplitEntry(str[_offset..], "");
                _offset = str.Length;
                return true;
            }

            index += _offset;
            if (index < str.Length - 1 && str[index] == '\r')
            {
                var next = str[index + 1];
                if (next == '\n')
                {
                    Current = new LineSplitEntry(str[_offset..index], "\r\n");
                    _offset = index + 2;
                    return true;
                }
            }

            Current = new LineSplitEntry(str[_offset..index], str[index] == '\r' ? "\r" : "\n");
            _offset = index + 1;
            return true;
        }

        public LineSplitEntry Current { get; private set; }
    }

    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public readonly struct LineSplitEntry
    {
        public LineSplitEntry(string line, string separator)
        {
            Line = line;
            Separator = separator;
        }

        public string Line { get; }
        public string Separator { get; }

        public void Deconstruct(out string line, out string separator)
        {
            line = Line;
            separator = Separator;
        }

        public static implicit operator string(LineSplitEntry entry) => entry.Line;
    }
}
