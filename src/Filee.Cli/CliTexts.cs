// Embedded CLI messages plus the app's conversion errors and built-in preset names. Language selection is
// read-only: help and argument errors must not create settings, run migrations or initialize engines.

using System.Globalization;
using System.Text.Json;
using Filee.App.Services;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.Cli;

internal sealed class CliTexts : ILocalizer
{
    internal static readonly string[] Languages = ["en", "pt-PT"];
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = Languages.ToDictionary(code => code, Load);
    public static readonly CliTexts English = new("en");
    private readonly Dictionary<string, string> _texts;

    public CliTexts(string language)
    {
        Language = language;
        _texts = Catalogs.GetValueOrDefault(language, Catalogs["en"]);
    }

    public string Language { get; }
    public string this[string key] => _texts.GetValueOrDefault(key, Catalogs["en"].GetValueOrDefault(key, key));
    public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);
    public event EventHandler? LanguageChanged { add { } remove { } }

    /// <summary>An explicit CLI choice wins over the saved app language; auto uses the OS display language.</summary>
    internal static string Resolve(string? requested, string? saved, Func<string> osLanguage)
    {
        var setting = requested ?? saved;
        if (string.IsNullOrWhiteSpace(setting) || setting.Equals("auto", StringComparison.OrdinalIgnoreCase))
            setting = osLanguage();
        return AppLanguages.PortugueseVariant(setting) == "pt-PT" ? "pt-PT" : "en";
    }

    public static CliTexts Create(string? requested, string? dataDirectory = null) =>
        new(Resolve(requested, requested is null ? ReadLanguage(dataDirectory ?? UserDataStore.DefaultDirectory) : null,
            () => Filee.Platform.MacOS.MacOSLocale.PreferredLanguage() ?? CultureInfo.CurrentUICulture.Name));

    /// <summary>Only inspect the language property. Loading UserDataStore here would migrate and write files.</summary>
    internal static string? ReadLanguage(string dataDirectory)
    {
        try
        {
            using var stream = File.OpenRead(Path.Combine(dataDirectory, "settings.json"));
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return json.RootElement.ValueKind == JsonValueKind.Object &&
                   json.RootElement.TryGetProperty("language", out var language) && language.ValueKind == JsonValueKind.String
                ? language.GetString() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> Load(string language)
    {
        var texts = new Dictionary<string, string>();
        foreach (var source in new[] { "App", "Text" })
        {
            using var stream = typeof(CliTexts).Assembly.GetManifestResourceStream($"Filee.Cli.{source}.{language}.json")
                               ?? throw new InvalidOperationException($"Embedded {source}/{language}.json is missing.");
            using var json = JsonDocument.Parse(stream);
            foreach (var property in json.RootElement.EnumerateObject())
                texts.Add(property.Name, property.Value.GetString()!);
        }
        return texts;
    }
}
