namespace Reshot.Core.Export;

/// <summary>
/// Builds output file names from the user's template (SPEC §13). Supports the
/// <c>{date}</c> and <c>{time}</c> placeholders; anything else is kept verbatim.
/// Pure and testable — no file-system access here.
/// </summary>
public static class FilenameBuilder
{
    public static string Build(string template, string extension, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(template))
            template = "reshot_{date}_{time}";

        var name = template
            .Replace("{date}", now.ToString("yyyy-MM-dd"))
            .Replace("{time}", now.ToString("HH-mm-ss"));

        var ext = extension.TrimStart('.').ToLowerInvariant();
        return $"{name}.{ext}";
    }
}
