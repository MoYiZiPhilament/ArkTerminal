using System.Text;

namespace ArkTerminal.Terminal.Sessions.Windows;

internal static class WindowsCommandLine
{
    internal static string Build(string executablePath, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, executablePath);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }
        return builder.ToString();
    }

    internal static string QuoteArgument(string argument, bool forCmd = false)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, argument, forceQuotes: true, forCmd: forCmd);
        return builder.ToString();
    }

    private static void AppendArgument(StringBuilder builder, string argument, bool forceQuotes = false, bool forCmd = false)
    {
        if (!forceQuotes && argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                slashes++;
                continue;
            }

            if (character == '"')
            {
                // Paired quotes keep cmd's quote state balanced; a native argv parser recovers the literal quote.
                builder.Append('\\', slashes * 2 + (forCmd ? 0 : 1));
                builder.Append(forCmd ? "\"\"" : "\"");
            }
            else
            {
                builder.Append('\\', slashes);
                builder.Append(character);
            }
            slashes = 0;
        }

        builder.Append('\\', slashes * 2);
        builder.Append('"');
    }
}
