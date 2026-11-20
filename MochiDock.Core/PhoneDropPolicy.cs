namespace MochiDock.Core;

public sealed record DropFile(string Name, long Size);
public static class PhoneDropPolicy
{
    public const long MaxFileBytes = 512L * 1024 * 1024;
    public const long MaxBatchBytes = 1024L * 1024 * 1024;
    public static DropFile[] Validate(IReadOnlyList<DropFile> files)
    {
        if (files.Count is < 1 or > 10) throw new ArgumentException("Choose between 1 and 10 files.");
        long total = 0;
        var result = new List<DropFile>();
        foreach (var file in files)
        {
            if (file is null) throw new ArgumentException("Invalid file entry.");
            if (file.Size < 0 || file.Size > MaxFileBytes) throw new ArgumentException("Each file must be at most 512 MiB.");
            total += file.Size;
            if (total > MaxBatchBytes) throw new ArgumentException("The request exceeds 1 GiB.");
            result.Add(file with { Name = SafeName(file.Name) });
        }
        return result.ToArray();
    }
    public static string SafeName(string name)
    {
        name = (name ?? "").Replace('\\', '/').Split('/').Last();
        name = new string(name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format ? '_' : c).ToArray()).Trim().TrimEnd('.',' ');
        if (name.Length > 160) name = name[..160].TrimEnd('.',' ');
        if (string.IsNullOrWhiteSpace(name)) name = "upload";
        string stem = name.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem) || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9')) name = "_" + name;
        return name;
    }
}
