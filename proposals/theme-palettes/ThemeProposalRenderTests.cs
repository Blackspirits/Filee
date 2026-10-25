// Review-only harness: copied into a temporary App.Tests project, never into the shipped application.

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Filee.App.Services;
using Filee.App.ViewModels;
using Filee.App.Views;
using Filee.Core.Localization;
using Filee.Core.Settings;

namespace Filee.App.Tests;

public sealed class ThemeProposalRenderTests
{
    [AvaloniaTheory]
    [InlineData("filee", ThemeMode.Light, ThemeStyle.Soft)]
    [InlineData("filee", ThemeMode.Dark, ThemeStyle.Soft)]
    [InlineData("catppuccin", ThemeMode.Light, ThemeStyle.Soft)]
    [InlineData("catppuccin", ThemeMode.Dark, ThemeStyle.Soft)]
    [InlineData("nord", ThemeMode.Light, ThemeStyle.Soft)]
    [InlineData("nord", ThemeMode.Dark, ThemeStyle.Soft)]
    [InlineData("dracula", ThemeMode.Light, ThemeStyle.Soft)]
    [InlineData("dracula", ThemeMode.Dark, ThemeStyle.Soft)]
    [InlineData("monokai", ThemeMode.Light, ThemeStyle.Soft)]
    [InlineData("monokai", ThemeMode.Dark, ThemeStyle.Soft)]
    [InlineData("filee", ThemeMode.Light, ThemeStyle.MaterialYou)]
    [InlineData("filee", ThemeMode.Dark, ThemeStyle.MaterialYou)]
    [InlineData("catppuccin", ThemeMode.Light, ThemeStyle.MaterialYou)]
    [InlineData("catppuccin", ThemeMode.Dark, ThemeStyle.MaterialYou)]
    [InlineData("nord", ThemeMode.Light, ThemeStyle.MaterialYou)]
    [InlineData("nord", ThemeMode.Dark, ThemeStyle.MaterialYou)]
    [InlineData("dracula", ThemeMode.Light, ThemeStyle.MaterialYou)]
    [InlineData("dracula", ThemeMode.Dark, ThemeStyle.MaterialYou)]
    [InlineData("monokai", ThemeMode.Light, ThemeStyle.MaterialYou)]
    [InlineData("monokai", ThemeMode.Dark, ThemeStyle.MaterialYou)]
    public void Palettes_load_apply_render_and_restore(string name, ThemeMode mode, ThemeStyle style)
    {
        const string language = "pt-PT";
        TestServices.EnsureInitialized(language);
        var store = AppHost.Get<UserDataStore>();
        var oldTheme = store.Settings.Theme;
        var service = AppHost.Get<ThemeService>();
        var theme = new ThemeSettings { Mode = mode, Style = style, Accent = "#7F77DD", ReduceMotion = true };
        store.Settings.Theme = theme;
        CustomTheme? custom = null;
        byte[]? sourceBefore = null;
        string? sourceFile = null;
        var variant = mode == ThemeMode.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (name != "filee")
        {
            var proposal = Environment.GetEnvironmentVariable("FILEE_PROPOSAL_ROOT")
                ?? throw new InvalidOperationException("Set FILEE_PROPOSAL_ROOT to this proposal folder.");
            sourceFile = Path.Combine(proposal, "examples", name + ".json");
            sourceBefore = File.ReadAllBytes(sourceFile);
            var directory = Directory.CreateTempSubdirectory("filee-palette-input-").FullName;
            File.Copy(sourceFile, Path.Combine(directory, CustomTheme.FileName));
            custom = CustomTheme.Load(directory);
            Assert.NotNull(custom);
            Assert.Equal(9, custom.Dark.Count);
            Assert.Equal(name == "monokai" ? 0 : 9, custom.Light.Count);
        }

        using var vm = new MainWindowViewModel(AppHost.Services, AppHost.Get<ILocalizer>(), AppHost.Get<UpdateService>());
        vm.Navigate("theme");
        var window = new MainWindow { DataContext = vm, Width = 1080, Height = 820 };
        try
        {
            service.Apply(theme, language);
            service.ApplyWindowClass(window);
            window.Show();
            RenderTests.Pump();
            var defaults = ThemeService.CustomBrushKeys.ToDictionary(pair => pair.Key, pair => BrushColor(window, pair.Value, variant));

            service.Apply(theme, language, custom);
            service.ApplyWindowClass(window);
            RenderTests.Pump();
            Assert.Equal(variant, window.ActualThemeVariant);
            Assert.Equal(style == ThemeStyle.MaterialYou, window.Classes.Contains("material-you"));
            var colors = mode == ThemeMode.Dark ? custom?.Dark : custom?.Light;
            foreach (var (key, resource) in ThemeService.CustomBrushKeys)
            {
                var expected = colors?.TryGetValue(key, out var value) == true ? Color.Parse(value) : defaults[key];
                Assert.Equal(expected, BrushColor(window, resource, variant));
            }
            RenderTests.Save(window, $"proposal-{name}-{mode}-{style}.png");

            service.Apply(theme, language);
            RenderTests.Pump();
            foreach (var (key, resource) in ThemeService.CustomBrushKeys)
                Assert.Equal(defaults[key], BrushColor(window, resource, variant));
            if (sourceFile is not null)
                Assert.Equal(sourceBefore, File.ReadAllBytes(sourceFile));
        }
        finally
        {
            window.Close();
            store.Settings.Theme = oldTheme;
            service.Apply(oldTheme, language);
        }
    }

    private static Color BrushColor(Window window, string key, ThemeVariant variant)
    {
        Assert.True(window.TryFindResource(key, variant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }
}
