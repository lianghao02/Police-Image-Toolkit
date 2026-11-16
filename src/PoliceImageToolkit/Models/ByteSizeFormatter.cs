namespace PoliceImageToolkit.Models;

public static class ByteSizeFormatter
{
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB"];

    public static string Format(long bytes)
    {
        int order = 0;
        double len = Math.Max(0, bytes);
        while (len >= 1024 && order < Suffixes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {Suffixes[order]}";
    }
}
