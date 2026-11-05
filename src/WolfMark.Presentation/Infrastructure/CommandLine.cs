using System.Text;

namespace WolfMark.App.Infrastructure;

/// <summary>
/// Splits a Windows command line into arguments as <c>CommandLineToArgvW</c> does (a redirected activation hands
/// the second instance's command line over as one string): spaces separate, quotes group, <c>2n</c> backslashes
/// before a quote are <c>n</c> backslashes, <c>2n+1</c> are <c>n</c> and a literal quote.
/// </summary>
internal static class CommandLine
{
    public static List<string> Split(string commandLine)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasArg = false;
        for (var i = 0; i < commandLine.Length; i++)
        {
            var c = commandLine[i];
            if (c == '\\')
            {
                var start = i;
                while (i < commandLine.Length && commandLine[i] == '\\')
                {
                    i++;
                }

                var count = i - start;
                if (i < commandLine.Length && commandLine[i] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else
                {
                    current.Append('\\', count);
                    i--;
                }

                hasArg = true;
            }
            else if (c == '"')
            {
                // "" inside quotes is a literal quote.
                if (inQuotes && i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                hasArg = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasArg)
                {
                    args.Add(current.ToString());
                    current.Clear();
                    hasArg = false;
                }
            }
            else
            {
                current.Append(c);
                hasArg = true;
            }
        }

        if (hasArg)
        {
            args.Add(current.ToString());
        }

        return args;
    }
}
