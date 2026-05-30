using System.Runtime.InteropServices;
using System.Text;

namespace WhisperLauncher;

/// <summary>
/// Thin wrapper around the Windows private-profile (INI) API.
/// Reads and writes key/value pairs while preserving comments and structure.
/// </summary>
internal static class IniManager
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPrivateProfileString(
        string lpAppName, string lpKeyName, string lpDefault,
        StringBuilder lpReturnedString, int nSize, string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(
        string lpAppName, string lpKeyName, string? lpString, string lpFileName);

    // ------------------------------------------------------------------ read

    public static string Read(string section, string key, string defaultValue, string iniPath)
    {
        var sb = new StringBuilder(2048);
        GetPrivateProfileString(section, key, defaultValue, sb, sb.Capacity, iniPath);
        return sb.ToString().Trim();
    }

    public static bool ReadBool(string section, string key, bool defaultValue, string iniPath)
    {
        string v = Read(section, key, defaultValue ? "true" : "false", iniPath).ToLowerInvariant();
        return v == "true" || v == "1" || v == "yes";
    }

    // ----------------------------------------------------------------- write

    public static void Write(string section, string key, string value, string iniPath)
        => WritePrivateProfileString(section, key, value, iniPath);

    public static void Write(string section, string key, bool value, string iniPath)
        => Write(section, key, value ? "true" : "false", iniPath);

    // --------------------------------------------------------- helper: ensure

    /// <summary>
    /// Writes <paramref name="defaultContent"/> to <paramref name="iniPath"/>
    /// only if the file does not already exist.
    /// </summary>
    public static void EnsureExists(string iniPath, string defaultContent)
    {
        if (!File.Exists(iniPath))
            File.WriteAllText(iniPath, defaultContent, Encoding.UTF8);
    }

    // -------------------------------------------------------- section listing

    /// <summary>Returns all section names in the INI file by parsing line-by-line.</summary>
    public static IReadOnlyList<string> GetSections(string iniPath)
    {
        if (!File.Exists(iniPath)) return Array.Empty<string>();
        var sections = new List<string>();
        foreach (string line in File.ReadLines(iniPath))
        {
            string t = line.Trim();
            if (t.StartsWith('[') && t.EndsWith(']'))
                sections.Add(t[1..^1].Trim());
        }
        return sections;
    }

    /// <summary>Returns all key names in a section.</summary>
    public static IReadOnlyList<string> GetKeys(string section, string iniPath)
    {
        var buf = new StringBuilder(8192);
        GetPrivateProfileString(section, null!, "", buf, buf.Capacity, iniPath);
        string raw = buf.ToString();
        if (raw.Length == 0) return Array.Empty<string>();
        // The API returns null-delimited, double-null-terminated strings
        return raw.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }
}
