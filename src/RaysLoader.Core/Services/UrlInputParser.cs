namespace RaysLoader.Services;

public static class UrlInputParser
{
    public static IReadOnlyList<Uri> Parse(string input)
    {
        var result = new List<Uri>();
        // AbsoluteUri already lowercases the scheme and host. Paths and queries stay
        // case-sensitive because many sites use case-sensitive media IDs.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(line, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new FormatException($"'{line}' is not a valid HTTP or HTTPS URL.");
            }

            if (seen.Add(uri.AbsoluteUri))
            {
                result.Add(uri);
            }
        }

        if (result.Count == 0)
        {
            throw new FormatException("Enter at least one media URL.");
        }

        return result;
    }
}
