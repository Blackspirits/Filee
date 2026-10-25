// The "filee" commands: convert, watch, formats, presets. Writes results for people (one line per file) or, with
// --json, one JSON document for other programs. Exit codes: 0 everything converted, 1 some files failed,
// 2 wrong arguments or a missing input, 3 nothing to convert.

using System.Text.Json;
using Filee.Core.Conversion;
using Filee.Core.Formats;
using Filee.Core.Localization;
using Filee.Core.Platform;
using Filee.Core.Presets;
using Filee.Core.Watching;

namespace Filee.Cli;

internal static class Cli
{
    public const int Success = 0;
    public const int SomeFailed = 1;
    public const int UsageError = 2;
    public const int NothingToDo = 3;

    /// <param name="createHost">Engines and settings, using the selected texts (tests use a temporary folder).</param>
    /// <param name="dataDirectory">An isolated profile, or the normal Filee data folder when null.</param>
    public static async Task<int> RunAsync(IReadOnlyList<string> args, TextWriter output, TextWriter errors,
        CancellationToken cancellationToken, Func<ILocalizer, CliHost>? createHost = null, string? dataDirectory = null)
    {
        var options = CliOptions.Parse(args);
        var texts = CliTexts.Create(options.Language, dataDirectory);
        if (options.Error is not null)
        {
            errors.WriteLine($"filee: {texts.Format(options.Error.Key, options.Error.Arguments)}");
            errors.WriteLine(texts["cli.run_help"]);
            return UsageError;
        }
        switch (options.Command)
        {
            case "help":
                output.Write(Help.Text(options.Inputs.FirstOrDefault(), texts));
                return Success;
            case "version":
                output.WriteLine($"filee {Help.Version}");
                return Success;
        }

        await using var host = createHost is null ? CliHost.Create(dataDirectory, texts) : createHost(texts);
        return options.Command switch
        {
            "convert" => await ConvertAsync(options, host, output, errors, cancellationToken),
            "watch" => await WatchAsync(options, host, output, errors, cancellationToken),
            "formats" => ListFormats(options, host, output, errors),
            _ => ListPresets(host, output),
        };
    }

    private static async Task<int> ConvertAsync(CliOptions options, CliHost host, TextWriter output, TextWriter errors, CancellationToken cancellationToken)
    {
        if (ResolvePreset(options, host, errors) is not { } preset)
            return UsageError;
        if (options.Output is not null)
            UseFolder(preset, options.Output);

        var missing = new List<string>();
        var files = ExpandInputs(options.Inputs, options.Recursive, missing);
        foreach (var path in missing)
            errors.WriteLine($"filee: {host.Texts.Format("cli.not_found", path)}");
        if (missing.Count > 0)
            return UsageError;
        if (files.Count == 0)
        {
            errors.WriteLine($"filee: {host.Texts["cli.no_files"]}");
            return NothingToDo;
        }

        var job = await RunJobAsync(files, preset, host, options.Json || options.Quiet ? null : output, cancellationToken);
        var done = job.Files.Count(f => f.State == FileState.Done);
        var failed = job.Files.Count(f => f.State is FileState.Failed or FileState.Cancelled);
        if (options.Json)
            WriteJson(job, output);
        else if (!options.Quiet)
            output.WriteLine(failed == 0
                ? host.Texts.Format("cli.converted", done, job.Files.Count)
                : host.Texts.Format("cli.converted_with_failures", done, job.Files.Count, failed));
        return failed == 0 ? Success : SomeFailed;
    }

    private static async Task<int> WatchAsync(CliOptions options, CliHost host, TextWriter output, TextWriter errors, CancellationToken cancellationToken)
    {
        if (ResolvePreset(options, host, errors) is not { } preset)
            return UsageError;
        var rule = new WatchRule
        {
            // Unlike saved app rules, a CLI rule is recreated on every launch. Keep its generated-result state
            // stable so an inbox writing into itself does not reprocess its outputs after restarting the command.
            Id = "cli",
            Folder = Path.GetFullPath(options.Inputs[0]),
            IncludeSubfolders = options.Recursive,
            OutputFolder = options.Output is null ? "" : Path.GetFullPath(options.Output),
            Originals = options.MoveOriginals ? AfterConversion.MoveToOriginals : AfterConversion.Keep,
        };
        UseFolder(preset, rule.ResolvedOutputFolder);
        preset.Output.KeepFoldersFrom = rule.Folder; // as the app's watch folders: subfolders stay subfolders

        var settle = options.SettleSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : (TimeSpan?)null;
        await using var watcher = new FolderWatcher(rule,
            async (files, token) => await RunJobAsync(files, preset, host, options.Quiet ? null : output, token),
            settleTime: settle);
        if (!watcher.Start())
        {
            var message = Directory.Exists(rule.Folder)
                ? host.Texts.Format("cli.watch_failed", rule.Folder, watcher.Error ?? "")
                : host.Texts.Format("cli.folder_not_found", rule.Folder);
            errors.WriteLine($"filee: {message}");
            return UsageError;
        }
        if (!options.Quiet)
            output.WriteLine(host.Texts.Format("cli.watching", rule.Folder, host.Texts.DisplayName(preset), rule.ResolvedOutputFolder));
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        return Success;
    }

    /// <summary>Runs one job and prints each file's result as soon as it is final (when <paramref name="output"/> is given).</summary>
    private static async Task<ConversionJob> RunJobAsync(IReadOnlyList<string> files, Preset preset, CliHost host, TextWriter? output,
        CancellationToken cancellationToken)
    {
        // The job name also expands {preset} in output filename patterns. Preserve the CLI's previous English
        // expansion even when its console messages are translated, so changing language never renames outputs.
        var job = new ConversionJob(files, preset, CliTexts.English.DisplayName(preset));
        var printed = new HashSet<FileResult>();
        void Print(object? sender, ConversionJob updated)
        {
            if (output is null || updated != job)
                return;
            lock (printed)
            {
                foreach (var file in job.Files.Where(f => f.State is not (FileState.Pending or FileState.Running) && printed.Add(f)))
                    output.WriteLine(Describe(file, host.Texts));
            }
        }

        host.Queue.JobUpdated += Print;
        try
        {
            using var _ = cancellationToken.Register(job.Cancel);
            await host.Queue.RunAsync(job);
        }
        finally
        {
            host.Queue.JobUpdated -= Print;
        }
        Print(null, job);
        return job;
    }

    private static string Describe(FileResult file, ILocalizer texts)
    {
        var source = Shorten(file.SourcePath);
        return file.State switch
        {
            FileState.Done => texts.Format("cli.file.done", source, string.Join(", ", file.Outputs.Select(Shorten)))
                + (file.NoticeKey is null ? "" : $" ({Notice(file, texts)})")
                + (file.MissingFonts.Count == 0 ? "" : $" ({texts.Format("cli.missing_fonts", string.Join(", ", file.MissingFonts))})"),
            FileState.Skipped => texts.Format("cli.file.skipped", source),
            FileState.Cancelled => texts.Format("cli.file.cancelled", source),
            _ => texts.Format("cli.file.failed", source, Error(file, texts)),
        };
    }

    private static string Error(FileResult file, ILocalizer texts)
    {
        var message = texts[file.ErrorKey ?? "cli.conversion_failed"];
        return string.IsNullOrWhiteSpace(file.ErrorDetail) ? message : $"{message} ({file.ErrorDetail})";
    }

    private static string Notice(FileResult file, ILocalizer texts) => texts.Notice(file);

    /// <summary>Relative to the current folder when the file is inside it, else the full path.</summary>
    private static string Shorten(string path)
    {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }

    private static void WriteJson(ConversionJob job, TextWriter output)
    {
        // JSON is a scripting contract. Keep even its preset names, errors and notices in English, as before
        // localization; language selection affects human-readable stdout/stderr only.
        var texts = CliTexts.English;
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            json.WriteStartObject();
            json.WriteString("preset", texts.DisplayName(job.Preset));
            json.WriteString("target", job.Preset.TargetFormat);
            json.WriteNumber("converted", job.Files.Count(f => f.State == FileState.Done));
            json.WriteNumber("skipped", job.Files.Count(f => f.State == FileState.Skipped));
            json.WriteNumber("failed", job.Files.Count(f => f.State is FileState.Failed or FileState.Cancelled));
            json.WriteStartArray("files");
            foreach (var file in job.Files)
            {
                json.WriteStartObject();
                json.WriteString("source", file.SourcePath);
                json.WriteString("state", file.State.ToString().ToLowerInvariant());
                json.WriteStartArray("outputs");
                foreach (var path in file.Outputs)
                    json.WriteStringValue(path);
                json.WriteEndArray();
                if (file.State is FileState.Failed or FileState.Cancelled)
                    json.WriteString("error", Error(file, texts));
                else if (file.NoticeKey is not null)
                    json.WriteString("notice", Notice(file, texts));
                if (file.MissingFonts.Count > 0)
                {
                    json.WriteStartArray("missingFonts");
                    foreach (var font in file.MissingFonts)
                        json.WriteStringValue(font);
                    json.WriteEndArray();
                }
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        output.WriteLine(System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    /// <summary>The preset from --to or --preset, with --quality and --overwrite/--skip applied (a copy).</summary>
    private static Preset? ResolvePreset(CliOptions options, CliHost host, TextWriter errors)
    {
        Preset preset;
        if (options.To is not null)
        {
            if (FindFormat(options.To) is not { } format)
            {
                errors.WriteLine($"filee: {host.Texts.Format("cli.unknown_target", options.To)}");
                return null;
            }
            if (!format.Writable)
            {
                errors.WriteLine($"filee: {host.Texts.Format("cli.cannot_write", format.DisplayName)}");
                return null;
            }
            preset = new Preset { TargetFormat = format.Id };
        }
        else
        {
            var wanted = options.Preset!;
            Preset? Find(IEnumerable<Preset> presets) =>
                presets.FirstOrDefault(p => string.Equals(p.Id, wanted, StringComparison.OrdinalIgnoreCase))
                ?? presets.FirstOrDefault(p => string.Equals(CliTexts.English.DisplayName(p), wanted, StringComparison.OrdinalIgnoreCase));
            // Conversions first: a group of the same name (say a "Rotate" group next to your own "Rotate" preset)
            // must not hide one. A group is only found to say what it holds.
            var conversions = host.Store.Presets.Where(p => !p.IsGroup && !p.IsTool);
            // Preserve all previous ID/English/custom-name matches, including a saved group or tool that must
            // be refused. Translated aliases are new and must not hide anything a command already selected.
            var found = Find(conversions) ?? Find(host.Store.Presets)
                ?? conversions.FirstOrDefault(p => string.Equals(host.Texts.DisplayName(p), wanted, StringComparison.OrdinalIgnoreCase))
                ?? host.Store.Presets.FirstOrDefault(p => string.Equals(host.Texts.DisplayName(p), wanted, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                errors.WriteLine($"filee: {host.Texts.Format("cli.no_preset", wanted)}");
                return null;
            }
            if (found.IsGroup)
            {
                // A donut group converts nothing; name what it holds so the right one is easy to pick.
                var members = found.SubPresetIds.Select(host.Store.FindConversion).OfType<Preset>().Select(p => p.Id).ToList();
                errors.WriteLine(members.Count > 0
                    ? $"filee: {host.Texts.Format("cli.preset_group", wanted, string.Join(", ", members))}"
                    : $"filee: {host.Texts.Format("cli.preset_group_empty", wanted)}");
                return null;
            }
            if (found.IsTool)
            {
                errors.WriteLine($"filee: {host.Texts.Format("cli.preset_tool", wanted)}");
                return null;
            }
            preset = found.Clone();
        }
        preset = host.Store.Settings.WithDefaultOutput(preset);
        if (options.Quality is { } quality)
            preset.Image.Quality = quality;
        if (options.Conflict is { } conflict)
            preset.Output.Conflict = conflict;
        if (options.KeepDates)
            preset.Output.KeepDates = true;
        if (options.Zip)
            preset.Output.ZipResult = true;
        return preset;
    }

    private static void UseFolder(Preset preset, string folder)
    {
        preset.Output.Location = OutputLocation.CustomFolder;
        preset.Output.CustomFolder = Path.GetFullPath(folder);
    }

    /// <summary>A format by id ("jpg") or by extension ("jpeg", ".jpg").</summary>
    internal static FileFormat? FindFormat(string text)
    {
        var key = text.Trim().TrimStart('.').ToLowerInvariant();
        return FormatRegistry.FindById(key) ?? FormatRegistry.FindByExtension(key);
    }

    /// <summary>
    /// Files from the arguments: files as given, wildcards ("*.heic", which cmd.exe and PowerShell pass on unexpanded)
    /// and folders (their files in formats Filee knows; with --recursive also in sub folders).
    /// </summary>
    internal static List<string> ExpandInputs(IEnumerable<string> inputs, bool recursive, List<string> missing)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (input.IndexOfAny(['*', '?']) >= 0)
            {
                var folder = Path.GetDirectoryName(input) is { Length: > 0 } dir ? dir : ".";
                var matches = Directory.Exists(folder)
                    ? Directory.EnumerateFiles(folder, Path.GetFileName(input), option).ToList()
                    : [];
                if (matches.Count == 0)
                    missing.Add(input);
                files.AddRange(matches);
            }
            else if (Directory.Exists(input))
                files.AddRange(Directory.EnumerateFiles(input, "*", option).Where(f => FormatRegistry.Detect(f) is not null));
            else if (File.Exists(input))
                files.Add(input);
            else
                missing.Add(input);
        }
        return files.Select(Path.GetFullPath).Distinct(FileSystemPaths.Comparer).ToList();
    }

    private static int ListFormats(CliOptions options, CliHost host, TextWriter output, TextWriter errors)
    {
        if (options.Inputs.FirstOrDefault() is { } name)
        {
            if (FindFormat(name) is not { } from)
            {
                errors.WriteLine($"filee: {host.Texts.Format("cli.unknown_format", name)}");
                return UsageError;
            }
            var installed = host.Catalog.CreatePlanner();
            // Engines a download would add, not those this platform lacks (JPEG XR is read with Windows' own codec).
            var everything = host.Catalog.CreatePlanner(host.Catalog.All
                .Where(c => host.Catalog.StatusOf(c).ReasonKey != "engine.reason.unsupported_platform")
                .Select(c => c.Id).ToList());
            var targets = FormatRegistry.Known.Where(t => t.Writable && t.Id != from.Id && t.Id != FormatRegistry.Folder).ToList();
            var now = targets.Where(t => installed.CanConvert(from.Id, t.Id)).Select(t => t.Id).ToList();
            var later = targets.Where(t => !now.Contains(t.Id) && everything.CanConvert(from.Id, t.Id)).Select(t => t.Id).ToList();
            output.WriteLine(host.Texts.Format("cli.converts_to", from.DisplayName, string.Join(", ", from.Extensions)));
            output.WriteLine(now.Count > 0 ? "  " + string.Join(", ", now) : "  " + host.Texts["cli.no_engines"]);
            if (later.Count > 0)
                output.WriteLine($"{host.Texts["cli.optional_engines"]}{Environment.NewLine}  {string.Join(", ", later)}");
            return Success;
        }

        foreach (var group in FormatRegistry.Known.Where(f => f.Id != FormatRegistry.Folder).GroupBy(f => f.Category))
        {
            var category = host.Texts.Language == "en" ? group.Key.ToString() : host.Texts[$"format.category.{group.Key}"];
            output.WriteLine($"{category}:");
            foreach (var format in group)
                output.WriteLine($"  {format.Id,-10} {format.DisplayName}{(format.Writable ? "" : " " + host.Texts["cli.read_only"])}  .{string.Join(" .", format.Extensions)}");
        }
        output.WriteLine();
        output.WriteLine(host.Texts["cli.formats_hint"]);
        return Success;
    }

    private static int ListPresets(CliHost host, TextWriter output)
    {
        output.WriteLine($"{host.Texts["cli.column.id"],-24} {host.Texts["cli.column.name"],-28} {host.Texts["cli.column.target"]}");
        foreach (var preset in host.Store.Presets.Where(p => !p.IsGroup && !p.IsTool))
            output.WriteLine($"{preset.Id,-24} {host.Texts.DisplayName(preset),-28} {preset.TargetFormat}");
        output.WriteLine();
        output.WriteLine(host.Texts["cli.presets_hint"]);
        return Success;
    }
}
