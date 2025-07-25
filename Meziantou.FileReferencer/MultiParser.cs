using System.Diagnostics;

namespace Meziantou.FileReferencer;

class MultiParser(bool endParserMatchStartParser, Parser[] parsers) : Parser
{
    private Parser? _currentParser;
    public override ReferenceMatch? MatchStart(string line)
    {
        foreach (var parser in parsers)
        {
            var match = parser.MatchStart(line);
            if (match is not null)
            {
                _currentParser = parser;
                return match;
            }
        }
        return null;
    }
    public override bool MatchEnd(string line)
    {
        if (endParserMatchStartParser)
        {
            Debug.Assert(_currentParser is not null);
            var result = _currentParser.MatchEnd(line);
            if (result)
            {
                _currentParser = null;
            }

            return result;
        }
        else
        {
            foreach (var parser in parsers)
            {
                if (parser.MatchEnd(line))
                {
                    _currentParser = null;
                    return true;
                }
            }

            return false;
        }
    }
}
