// Parses the "filee" command line. Kept dependency-free: a handful of commands and options.

using Filee.Core.Presets;

namespace Filee.Cli;

/// <summary>An argument error that can be translated after the language option has been parsed.</summary>
internal sealed record CliError(string Key, params object[] Arguments);

/// <summary>A parsed command line. <see cref="Error"/> is set when the arguments are wrong.</summary>
internal sealed record CliOptions
{
    public string Command { get; init; } = "help";
    public List<string> Inputs { get; } = [];
    public string? To { get; set; }
    public string? Preset { get; set; }
    public string? Output { get; set; }
    public bool Recursive { get; set; }
    public int? Quality { get; set; }
    public ConflictPolicy? Conflict { get; set; }
    public bool Json { get; set; }
    public bool Quiet { get; set; }
    public bool MoveOriginals { get; set; }
    public bool KeepDates { get; set; }

    public bool Zip { get; set; }
    public double? SettleSeconds { get; set; }
    public string? Language { get; set; }
    public CliError? Error { get; set; }

    public static readonly string[] Commands = ["convert", "watch", "formats", "presets", "help", "version"];

    public static CliOptions Parse(IReadOnlyList<string> args)
    {
        var options = new CliOptions();
        var start = 0;
        while (start < args.Count && args[start].Equals("--language", StringComparison.OrdinalIgnoreCase))
        {
            if (start + 1 == args.Count)
            {
                options.Error = new("cli.args.needs_value", args[start]);
                return options;
            }
            options.Language = args[start + 1];
            start += 2;
        }
        if (start == args.Count)
            return ValidateLanguage(options);
        var first = args[start].ToLowerInvariant();
        var command = first switch
        {
            "-h" or "--help" or "/?" => "help",
            "-v" or "--version" => "version",
            _ => first,
        };
        options = options with { Command = command };
        if (!Commands.Contains(command))
            options.Error = new("cli.args.unknown_command", args[start]);

        var helpRequested = false;
        for (var i = start + 1; i < args.Count; i++)
        {
            var arg = args[i];
            // Help used to stop parsing here. Still ignore trailing conversion arguments, but allow the
            // language choice after --help too (filee convert --help --language pt-PT).
            if (helpRequested && !arg.Equals("--language", StringComparison.OrdinalIgnoreCase))
                continue;
            string? Value()
            {
                if (i + 1 < args.Count)
                    return args[++i];
                options.Error ??= new("cli.args.needs_value", arg);
                return null;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--language":
                    options.Language = Value() ?? options.Language;
                    break;
                case "-t" or "--to":
                    options.To = Value();
                    break;
                case "-p" or "--preset":
                    options.Preset = Value();
                    break;
                case "-o" or "--output":
                    options.Output = Value();
                    break;
                case "-r" or "--recursive":
                    options.Recursive = true;
                    break;
                case "-q" or "--quality":
                    var quality = Value();
                    if (quality is not null)
                    {
                        if (int.TryParse(quality, out var q) && q is >= 1 and <= 100)
                            options.Quality = q;
                        else
                            options.Error ??= new("cli.args.quality");
                    }
                    break;
                case "--overwrite":
                    options.Conflict = ConflictPolicy.Overwrite;
                    break;
                case "--skip":
                    options.Conflict = ConflictPolicy.Skip;
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "--move-originals":
                    options.MoveOriginals = true;
                    break;
                case "--keep-dates":
                    options.KeepDates = true;
                    break;
                case "--zip":
                    options.Zip = true;
                    break;
                case "--settle":
                    var settle = Value();
                    if (settle is not null)
                    {
                        if (double.TryParse(settle, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) && s >= 0)
                            options.SettleSeconds = s;
                        else
                            options.Error ??= new("cli.args.settle");
                    }
                    break;
                case "-h" or "--help":
                    options.Inputs.Insert(0, options.Command);
                    options = options with { Command = "help" };
                    helpRequested = true;
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal) || (arg.StartsWith('-') && arg.Length == 2))
                        options.Error ??= new("cli.args.unknown_option", arg);
                    else
                        options.Inputs.Add(arg);
                    break;
            }
        }

        options.Error ??= options.Command switch
        {
            "convert" when options.Inputs.Count == 0 => new("cli.args.inputs"),
            "convert" or "watch" when options.To is null == (options.Preset is null) => new("cli.args.target"),
            "watch" when options.Inputs.Count != 1 => new("cli.args.watch_folder"),
            _ => null,
        };
        return ValidateLanguage(options);
    }

    private static CliOptions ValidateLanguage(CliOptions options)
    {
        if (options.Language is not null && !options.Language.Equals("auto", StringComparison.OrdinalIgnoreCase) &&
            !CliTexts.Languages.Contains(options.Language, StringComparer.OrdinalIgnoreCase))
            options.Error ??= new("cli.args.language", options.Language);
        return options;
    }
}
