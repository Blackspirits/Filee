// Usage text of the "filee" command, from its embedded CLI catalog.

using System.Reflection;
using Filee.Core.Localization;
using Filee.Core.Watching;

namespace Filee.Cli;

internal static class Help
{
    public static string Version =>
        typeof(Help).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    public static string Text(string? command, ILocalizer texts)
    {
        var key = command?.ToLowerInvariant() switch
        {
            "convert" => "cli.help.convert",
            "watch" => "cli.help.watch",
            "formats" => "cli.help.formats",
            "presets" => "cli.help.presets",
            _ => "cli.help.general",
        };
        return key == "cli.help.watch" ? texts.Format(key, FolderWatcher.DefaultSettleTime.TotalSeconds) : texts[key];
    }
}
