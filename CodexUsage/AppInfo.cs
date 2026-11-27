using System;
using System.IO;

namespace CodexUsage;

/// <summary>Identifiers and paths shared by the extension.</summary>
internal static class AppInfo
{
    public const string DisplayName = "Codex Usage Dock";

    public const string ProviderId = "codexusagedock";
    /// <summary>
    /// Dock band ID. Command Palette resolves pinned bands by this ID alone, ignoring the provider,
    /// so the Store and sideloaded builds use different IDs. Otherwise a pin left over from one
    /// build makes Command Palette treat the other build's band as already pinned and hide it
    /// from the Dock's Edit list.
    /// </summary>
    public static string BandId => s_bandId.Value;

    private static readonly Lazy<string> s_bandId = new(() => IsStoreBuild() ? "codexusagedock.band" : "codexusagedock.sideloaded.band");
    public const string FiveHourItemId = "codexusagedock.fivehour";
    public const string WeeklyItemId = "codexusagedock.weekly";
    public const string DetailsId = "codexusagedock.details";
    public const string UsageUrl = "https://chatgpt.com/codex/settings/usage";

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string DataDirectory => Path.Combine(Home, ".codex-usage-dock");

    public static string SessionsDirectory =>
        Environment.GetEnvironmentVariable("CODEX_USAGE_DOCK_SESSIONS_DIR") is { } custom && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : Path.Combine(Home, ".codex", "sessions");

    public static string Glyph(int codepoint) => ((char)codepoint).ToString();

    private static bool IsStoreBuild()
    {
        try
        {
            return Windows.ApplicationModel.Package.Current.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.Store;
        }
        catch
        {
            return false; // not running with package identity
        }
    }
}
