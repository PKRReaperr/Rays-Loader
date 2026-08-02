using System.Text;

namespace RaysLoader.Services;

public static class ArgumentTokenizer
{
    public static IReadOnlyList<string> Tokenize(string value)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        foreach (var character in value)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\' && inQuotes)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddCurrent(arguments, current);
                continue;
            }

            current.Append(character);
        }

        if (inQuotes)
        {
            throw new FormatException("Advanced arguments contain an unmatched quote.");
        }

        if (escaping)
        {
            current.Append('\\');
        }

        AddCurrent(arguments, current);
        return arguments;
    }

    private static void AddCurrent(List<string> arguments, StringBuilder current)
    {
        if (current.Length == 0)
        {
            return;
        }

        arguments.Add(current.ToString());
        current.Clear();
    }
}
