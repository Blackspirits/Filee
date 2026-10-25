// Localization contracts and real CLI runs in isolated profiles: human output can change language without
// changing command syntax, JSON, filenames, exit codes or the saved app settings.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Filee.Core.Presets;
using Filee.Core.Settings;

namespace Filee.Cli.Tests;

public sealed class LocalizationTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("filee-cli-i18n-").FullName;
    private string SettingsDirectory => Path.Combine(_folder, "profile");

    public void Dispose()
    {
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private Task<int> Run(IReadOnlyList<string> args, TextWriter output, TextWriter? errors = null,
        CancellationToken? cancellation = null) =>
        Cli.RunAsync(args, output, errors ?? TextWriter.Null, cancellation ?? TestContext.Current.CancellationToken,
            dataDirectory: SettingsDirectory);

    private string Png(string name)
    {
        var path = Path.Combine(_folder, name);
        using var image = new ImageMagick.MagickImage(ImageMagick.MagickColors.Orange, 40, 30);
        image.Write(path, ImageMagick.MagickFormat.Png);
        return path;
    }

    [Theory]
    [InlineData(null, "pt-PT", "en-US", "pt-PT")]
    [InlineData(null, "en", "pt-PT", "en")]
    [InlineData(null, "pt-BR", "pt-PT", "en")]
    [InlineData(null, "fr", "pt-PT", "en")]
    [InlineData("en", "pt-PT", "pt-PT", "en")]
    [InlineData("PT-pt", "pt-BR", "en-US", "pt-PT")]
    [InlineData("auto", "pt-PT", "en-US", "en")]
    [InlineData(null, "auto", "pt", "pt-PT")]
    [InlineData(null, null, "pt-AO", "pt-PT")]
    [InlineData(null, "auto", "pt_Latn_PT", "pt-PT")]
    [InlineData(null, "auto", "pt-Latn-BR", "en")]
    [InlineData(null, "auto", "pt-BR", "en")]
    [InlineData(null, "auto", "pt-PT-x-br", "pt-PT")]
    [InlineData(null, "auto", "pt-x-br", "pt-PT")]
    [InlineData(null, "auto", "pter", "en")]
    [InlineData(null, "auto", "", "en")]
    public void Language_selection_respects_saved_explicit_and_regional_choices(string? requested, string? saved,
        string system, string expected) => Assert.Equal(expected, CliTexts.Resolve(requested, saved, () => system));

    [Theory]
    [InlineData(new[] { "--language", "pt-PT", "help" }, "converte ficheiros")]
    [InlineData(new[] { "help", "convert", "--language", "pt-PT" }, "predefinição da aplicação")]
    [InlineData(new[] { "--language", "pt-PT", "help", "watch" }, "subpasta «converted»")]
    [InlineData(new[] { "help", "formats", "--language", "pt-PT" }, "Lista todos os formatos")]
    [InlineData(new[] { "help", "presets", "--language", "pt-PT" }, "as de origem e as tuas")]
    [InlineData(new[] { "convert", "--help", "--language", "pt-PT" }, "ficheiros que o Filee reconhece")]
    [InlineData(new[] { "--language", "pt-PT" }, "Códigos de saída")]
    public async Task Portuguese_help_keeps_commands_and_does_not_initialize_engines_or_create_a_profile(string[] args, string phrase)
    {
        // A help regression must not silently initialize a real host or modify a user's files.
        var output = new StringWriter();
        var code = await Cli.RunAsync(args, output, TextWriter.Null, TestContext.Current.CancellationToken,
            _ => throw new InvalidOperationException("Help initialized the engines"), SettingsDirectory);
        Assert.Equal(Cli.Success, code);
        Assert.Contains(phrase, output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("filee", output.ToString());
        Assert.Contains("--language", output.ToString());
        Assert.False(Directory.Exists(SettingsDirectory));
    }

    [Theory]
    [InlineData(new[] { "--language", "pt-PT", "unknown" }, "Comando desconhecido")]
    [InlineData(new[] { "unknown", "--language", "pt-PT" }, "Comando desconhecido")]
    [InlineData(new[] { "convert", "a.png", "--language", "pt-PT", "--bad" }, "Opção desconhecida")]
    [InlineData(new[] { "convert", "--language", "pt-PT", "--to", "jpg" }, "pelo menos um ficheiro")]
    [InlineData(new[] { "convert", "a.png", "--language", "pt-PT" }, "--to <formato>")]
    [InlineData(new[] { "convert", "a.png", "--to", "jpg", "--language", "pt-PT", "--quality", "300" }, "--quality tem de ser")]
    [InlineData(new[] { "watch", "folder", "--to", "jpg", "--language", "pt-PT", "--settle", "1,5" }, "--settle tem de ser")]
    [InlineData(new[] { "watch", "--to", "jpg", "--language", "pt-PT" }, "uma única pasta")]
    [InlineData(new[] { "convert", "a.png", "--language", "pt-PT", "--to" }, "--to precisa de um valor")]
    [InlineData(new[] { "--language", "pt-PT", "--language" }, "--language precisa de um valor")]
    [InlineData(new[] { "help", "--language", "pt-PT", "--language" }, "--language precisa de um valor")]
    public async Task Argument_errors_are_localized_before_any_host_is_created(string[] args, string phrase)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var code = await Cli.RunAsync(args, output, errors, TestContext.Current.CancellationToken,
            _ => throw new InvalidOperationException("Invalid arguments initialized the engines"), SettingsDirectory);
        Assert.Equal(Cli.UsageError, code);
        Assert.Equal("", output.ToString());
        Assert.Contains(phrase, errors.ToString());
        Assert.Contains("filee help", errors.ToString());
        Assert.False(Directory.Exists(SettingsDirectory));
    }

    [Theory]
    [InlineData("pt-BR")]
    [InlineData("es")]
    [InlineData("unknown")]
    public async Task Explicit_unsupported_languages_are_reported_instead_of_silently_selecting_Portuguese(string language)
    {
        var output = new StringWriter();
        var errors = new StringWriter();
        var code = await Run(["--language", language, "help"], output, errors);
        Assert.Equal(Cli.UsageError, code);
        Assert.Equal("", output.ToString());
        Assert.Contains($"Unsupported CLI language '{language}'", errors.ToString());
    }

    [Theory]
    [InlineData("pt-PT", "Utilização:")]
    [InlineData("pt-BR", "Usage:")]
    [InlineData("en", "Usage:")]
    public async Task Help_reads_the_saved_language_without_rewriting_settings_or_loading_the_library(string language, string phrase)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var path = Path.Combine(SettingsDirectory, "settings.json");
        var content = Encoding.UTF8.GetBytes($$"""{"schemaVersion":1,"language":"{{language}}","futureSetting":"keep me"}""");
        await File.WriteAllBytesAsync(path, content, TestContext.Current.CancellationToken);
        var output = new StringWriter();

        Assert.Equal(Cli.Success, await Run(["help"], output));
        Assert.Contains(phrase, output.ToString());
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["settings.json"], Directory.EnumerateFiles(SettingsDirectory).Select(Path.GetFileName));

        output.GetStringBuilder().Clear();
        Assert.Equal(Cli.Success, await Run(["--language", "en", "help"], output));
        Assert.Contains("Usage:", output.ToString());
        Assert.Equal(content, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"language\":123}")]
    public void An_unreadable_language_setting_falls_back_without_modifying_the_file(string content)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var path = Path.Combine(SettingsDirectory, "settings.json");
        File.WriteAllText(path, content);
        Assert.Null(CliTexts.ReadLanguage(SettingsDirectory));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Language_settings_accept_comments_and_trailing_commas_like_the_app()
    {
        Directory.CreateDirectory(SettingsDirectory);
        var path = Path.Combine(SettingsDirectory, "settings.json");
        const string content = "{ /* my settings */ \"language\": \"pt-PT\", }";
        File.WriteAllText(path, content);
        Assert.Equal("pt-PT", CliTexts.ReadLanguage(SettingsDirectory));
        Assert.Equal(content, File.ReadAllText(path));
    }

    public static IEnumerable<object[]> CatalogLanguages => CliTexts.Languages.Select(language => new object[] { language });

    [Theory]
    [MemberData(nameof(CatalogLanguages))]
    public void Catalogs_keep_keys_format_items_and_command_switches(string language)
    {
        static Dictionary<string, string> Read(string code)
        {
            using var stream = typeof(CliTexts).Assembly.GetManifestResourceStream($"Filee.Cli.Text.{code}.json");
            Assert.NotNull(stream);
            using var json = JsonDocument.Parse(stream);
            var properties = json.RootElement.EnumerateObject().ToList();
            Assert.Equal(properties.Count, properties.Select(p => p.Name).Distinct().Count());
            return properties.ToDictionary(p => p.Name, p => p.Value.GetString()!);
        }
        static string[] Matches(string text, string pattern) => [.. Regex.Matches(text, pattern).Select(m => m.Value).Order()];
        var english = Read("en");
        var translated = Read(language);
        Assert.Equal(english.Keys.Order(), translated.Keys.Order());
        foreach (var (key, value) in english)
        {
            var translation = translated[key];
            Assert.True(translation.IsNormalized(NormalizationForm.FormC), key);
            Assert.Equal(Matches(value, @"\{\d+\}"), Matches(translation, @"\{\d+\}"));
            Assert.Equal(Matches(value, @"(?<!\w)--[a-z-]+\b"), Matches(translation, @"(?<!\w)--[a-z-]+\b"));
            _ = string.Format(CultureInfo.InvariantCulture, translation, "first", "second", "third");
        }
        Assert.Equal("unknown.test.key", new CliTexts(language)["unknown.test.key"]);
        var watch = Help.Text("watch", new CliTexts(language));
        Assert.Contains("converted", watch);
        Assert.Contains("originals", watch);
        Assert.Contains((OperatingSystem.IsWindows() ? 2 : 5).ToString(CultureInfo.InvariantCulture), watch);
    }

    [Fact]
    public void Language_selection_does_not_change_regional_culture_or_numeric_option_syntax()
    {
        var original = CultureInfo.CurrentCulture;
        var originalUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-PT");
            var texts = CliTexts.Create("en");
            Assert.Equal("1,5", texts.Format("{0}", 1.5));
            var options = CliOptions.Parse(["watch", "folder", "--to", "pdf", "--settle", "1.5", "--language", "pt-PT"]);
            Assert.Null(options.Error);
            Assert.Equal(1.5, options.SettleSeconds);
            Assert.Equal("pt-PT", CultureInfo.CurrentCulture.Name);
            Assert.Same(originalUi, CultureInfo.CurrentUICulture);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void A_language_option_inside_another_options_value_is_not_reinterpreted()
    {
        var options = CliOptions.Parse(["convert", "a.png", "--preset", "--language"]);
        Assert.Null(options.Error);
        Assert.Null(options.Language);
        Assert.Equal("--language", options.Preset);
    }

    [Fact]
    public async Task Converts_Unicode_paths_and_reports_the_result_in_Portuguese()
    {
        var input = Png("ação d'água.png");
        var outputDirectory = Path.Combine(_folder, "resultado com espaços");
        var output = new StringWriter();
        var errors = new StringWriter();
        var code = await Run(["convert", input, "--to", "jpg", "-o", outputDirectory, "--language", "pt-PT"], output, errors);
        Assert.Equal(Cli.Success, code);
        Assert.Equal("", errors.ToString());
        Assert.Contains("Ficheiros convertidos: 1 de 1.", output.ToString());
        Assert.Contains("ação d'água.png", output.ToString());
        using var converted = new ImageMagick.MagickImage(Path.Combine(outputDirectory, "ação d'água.jpg"));
        Assert.Equal(ImageMagick.MagickFormat.Jpeg, converted.Format);
    }

    [Fact]
    public async Task Portuguese_failures_reuse_the_apps_translation_and_keep_exit_code_1()
    {
        var input = Path.Combine(_folder, "vazio.docx");
        File.WriteAllBytes(input, []);
        var output = new StringWriter();
        var code = await Run(["--language", "pt-PT", "convert", input, "--to", "pdf"], output);
        Assert.Equal(Cli.SomeFailed, code);
        Assert.Contains("O ficheiro está vazio (0 bytes)", output.ToString());
        Assert.Contains("Ficheiros convertidos: 0 de 1; falhas: 1.", output.ToString());
    }

    [Theory]
    [InlineData("missing-file", "não encontrado")]
    [InlineData("missing-watch", "Pasta não encontrada")]
    [InlineData("empty", "não há ficheiros para converter")]
    [InlineData("unknown-format", "formato desconhecido")]
    [InlineData("unknown-preset", "não existe a predefinição")]
    [InlineData("group", "é um grupo da barra circular")]
    [InlineData("tool", "é uma ferramenta interativa")]
    public async Task Other_errors_preserve_usage_and_nothing_to_do_exit_codes(string scenario, string phrase)
    {
        string[] args = scenario switch
        {
            "missing-file" => ["convert", Path.Combine(_folder, "missing.png"), "--to", "jpg"],
            "missing-watch" => ["watch", Path.Combine(_folder, "missing"), "--to", "jpg"],
            "empty" => ["convert", Directory.CreateDirectory(Path.Combine(_folder, "empty")).FullName, "--to", "jpg"],
            "unknown-format" => ["formats", "not-a-format"],
            "unknown-preset" => ["convert", "unused.png", "--preset", "not-a-preset"],
            "group" => ["convert", "unused.png", "--preset", "rotate"],
            _ => ["convert", "unused.png", "--preset", "tool-crop"],
        };
        var output = new StringWriter();
        var errors = new StringWriter();
        var code = await Run(["--language", "pt-PT", .. args], output, errors);
        Assert.Equal(scenario == "empty" ? Cli.NothingToDo : Cli.UsageError, code);
        Assert.Contains(phrase, errors.ToString());
    }

    [Fact]
    public async Task Lists_localized_categories_and_presets_without_changing_identifiers_or_custom_names()
    {
        var store = new UserDataStore(SettingsDirectory);
        store.Load();
        store.Presets.Add(new Preset { Id = "my-preset", Name = "My custom name", TargetFormat = "jpg" });
        store.SaveLibrary();
        var output = new StringWriter();
        Assert.Equal(Cli.Success, await Run(["--language", "pt-PT", "formats"], output));
        Assert.Contains("Folha de cálculo:", output.ToString());
        Assert.Contains("(só de leitura)", output.ToString());
        Assert.Contains("jpg", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(Cli.Success, await Run(["--language", "pt-PT", "presets"], output));
        Assert.Contains("NOME", output.ToString());
        Assert.Contains("DESTINO", output.ToString());
        Assert.Contains("Escala de cinzentos", output.ToString());
        Assert.Contains("grayscale", output.ToString());
        Assert.Contains("My custom name", output.ToString());
    }

    [Theory]
    [InlineData("Grayscale")]
    [InlineData("Escala de cinzentos")]
    [InlineData("grayscale")]
    public async Task English_and_Portuguese_names_and_stable_ids_find_the_same_preset(string name)
    {
        var input = Png("color.png");
        var output = new StringWriter();
        Assert.Equal(Cli.Success, await Run(["--language", "pt-PT", "convert", input, "--preset", name, "--json"], output));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("Grayscale", json.RootElement.GetProperty("preset").GetString());
        using var image = new ImageMagick.MagickImage(json.RootElement.GetProperty("files")[0].GetProperty("outputs")[0].GetString()!);
        Assert.Equal(ImageMagick.ColorSpace.Gray, image.ColorSpace);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("pt-PT")]
    public async Task A_saved_custom_name_wins_over_a_new_translated_builtin_alias(string language)
    {
        var store = new UserDataStore(SettingsDirectory);
        store.Load();
        store.Presets.Add(new Preset { Id = "my-half", Name = "Metade do tamanho", TargetFormat = "webp" });
        store.SaveLibrary();
        var input = Png("full-size.png");
        var output = new StringWriter();

        Assert.Equal(Cli.Success, await Run(["--language", language, "convert", input, "--preset", "Metade do tamanho", "--json"], output));

        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("webp", json.RootElement.GetProperty("target").GetString());
        Assert.Equal("Metade do tamanho", json.RootElement.GetProperty("preset").GetString());
        using var image = new ImageMagick.MagickImage(json.RootElement.GetProperty("files")[0].GetProperty("outputs")[0].GetString()!);
        Assert.Equal(ImageMagick.MagickFormat.WebP, image.Format);
        Assert.Equal(40u, image.Width);
        Assert.Equal(30u, image.Height);
    }

    [Theory]
    [InlineData(PresetKind.Group, false)]
    [InlineData(PresetKind.Group, true)]
    [InlineData(PresetKind.Tool, false)]
    [InlineData(PresetKind.Tool, true)]
    public async Task A_saved_group_or_tool_match_is_not_hidden_by_a_translated_conversion_alias(PresetKind kind, bool byId)
    {
        var store = new UserDataStore(SettingsDirectory);
        store.Load();
        store.Presets.Add(new Preset
        {
            Id = byId ? "Metade do tamanho" : "my-special-preset",
            Name = byId ? "My saved preset" : "Metade do tamanho",
            Kind = kind,
        });
        store.SaveLibrary();
        var input = Png("unchanged.png");
        var output = new StringWriter();
        var errors = new StringWriter();

        var code = await Run(["--language", "pt-PT", "convert", input, "--preset", "Metade do tamanho"], output, errors);

        Assert.Equal(Cli.UsageError, code);
        Assert.Equal("", output.ToString());
        Assert.Contains(kind == PresetKind.Group ? "é um grupo da barra circular" : "é uma ferramenta interativa", errors.ToString());
        Assert.Equal([input], Directory.GetFiles(_folder, "*.png"));
    }

    [Fact]
    public async Task An_existing_id_still_wins_over_a_saved_name_that_is_the_same_text()
    {
        var store = new UserDataStore(SettingsDirectory);
        store.Load();
        store.Presets.Add(new Preset { Id = "my-custom-half", Name = "half-size", TargetFormat = "webp" });
        store.SaveLibrary();
        var input = Png("original.png");
        var output = new StringWriter();

        Assert.Equal(Cli.Success, await Run(["--language", "pt-PT", "convert", input, "--preset", "half-size", "--json"], output));

        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(BuiltInData.SameAsSource, json.RootElement.GetProperty("target").GetString());
        using var image = new ImageMagick.MagickImage(json.RootElement.GetProperty("files")[0].GetProperty("outputs")[0].GetString()!);
        Assert.Equal(20u, image.Width);
        Assert.Equal(15u, image.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Json_is_identical_in_both_languages_including_English_errors_and_names(bool fail)
    {
        var input = fail ? Path.Combine(_folder, "empty.docx") : Png("source.png");
        if (fail)
            File.WriteAllBytes(input, []);
        var args = fail ? new[] { "convert", input, "--to", "pdf", "--json" }
            : ["convert", input, "--preset", "grayscale", "--overwrite", "--json"];
        var english = new StringWriter();
        var portuguese = new StringWriter();
        var englishCode = await Run(["--language", "en", .. args], english);
        var portugueseCode = await Run(["--language", "pt-PT", .. args], portuguese);
        Assert.Equal(englishCode, portugueseCode);
        Assert.Equal(english.ToString(), portuguese.ToString());
        using var json = JsonDocument.Parse(portuguese.ToString());
        if (fail)
            Assert.Contains("The file is empty", json.RootElement.GetProperty("files")[0].GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_preset_token_in_output_names_keeps_its_previous_English_expansion_in_both_output_modes()
    {
        var store = new UserDataStore(SettingsDirectory);
        store.Load();
        var preset = store.Presets.Single(p => p.Id == "grayscale");
        preset.Output.FileNamePattern = "{name}_{preset}";
        store.SaveLibrary();
        var input = Png("photo.png");
        var output = new StringWriter();
        var args = new[] { "--language", "pt-PT", "convert", input, "--preset", "grayscale", "--overwrite" };

        Assert.Equal(Cli.Success, await Run(args, output));
        var expected = Path.Combine(_folder, "photo_Grayscale.png");
        Assert.True(File.Exists(expected), output.ToString());
        Assert.Contains("photo_Grayscale.png", output.ToString());
        output.GetStringBuilder().Clear();
        Assert.Equal(Cli.Success, await Run([.. args, "--json"], output));
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(expected, json.RootElement.GetProperty("files")[0].GetProperty("outputs")[0].GetString());
        Assert.Equal([expected], Directory.GetFiles(_folder, "photo_*.png"));
    }

    [Fact]
    public async Task Quiet_suppresses_success_output_in_Portuguese_too()
    {
        var source = Png("quiet.png");
        var output = new StringWriter();
        Assert.Equal(Cli.Success, await Run(["--language", "pt-PT", "convert", source, "--to", "jpg", "--quiet"], output));
        Assert.Equal("", output.ToString());
        Assert.True(File.Exists(Path.Combine(_folder, "quiet.jpg")));
    }

    [Fact]
    public async Task Portuguese_watch_converts_arrivals_and_stops_without_translating_output_directories()
    {
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var output = new StringWriter();
        var watching = Run(["--language", "pt-PT", "watch", _folder, "--to", "jpg", "--settle", "0.1"],
            TextWriter.Synchronized(output), cancellation: stop.Token);
        try
        {
            Png("chegou.png");
            var converted = Path.Combine(_folder, "converted", "chegou.jpg");
            for (var i = 0; i < 100 && !File.Exists(converted); i++)
                await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(converted), output.ToString());
        }
        finally
        {
            stop.Cancel();
            await watching;
        }
        Assert.Equal(Cli.Success, await watching);
        Assert.Contains("A monitorizar", output.ToString());
        Assert.Contains("Ctrl+C", output.ToString());
    }
}
