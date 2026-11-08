using NTech.DataStudio.Application.Settings;
using NTech.DataStudio.Storage.Settings;

namespace NTech.DataStudio.Application.Tests.Settings;

/// <summary>Decision 0015: rebindable shortcuts.</summary>
public class ShortcutSettingsTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string SettingsPath => Path.Combine(_temp.Path, "nested", "settings.json");

    private SettingsService NewService() => new(new JsonSettingsStore(SettingsPath));

    private static Shortcut Key(string text)
    {
        Assert.True(Shortcut.TryParse(text, out var shortcut));
        return shortcut;
    }

    [Fact]
    public void LastProject_SurvivesARestartAndAClose()
    {
        var first = NewService();
        Assert.Null(first.LastProject);

        first.RememberProject(@"D:\data\wire");
        Assert.Equal(@"D:\data\wire", NewService().LastProject);

        first.RememberProject(null);
        Assert.Null(NewService().LastProject);
    }

    [Fact]
    public void LastProject_AndShortcutsDoNotEraseEachOther()
    {
        var service = NewService();
        service.RememberProject(@"D:\data\wire");
        Assert.Null(service.Assign(ShortcutAction.BoxTool, Key("K")));
        service.RememberProject(@"D:\data\other");

        var again = NewService();
        Assert.Equal(@"D:\data\other", again.LastProject);
        Assert.Equal(new[] { Key("K") }, again.Shortcuts.For(ShortcutAction.BoxTool));
    }

    [Fact]
    public void Defaults_NeverGiveOneKeyToTwoActions()
    {
        var seen = new Dictionary<Shortcut, ShortcutAction>();
        foreach (var info in ShortcutCatalog.All)
        {
            Assert.NotEmpty(info.Defaults);
            foreach (var key in info.Defaults)
            {
                Assert.Null(key.Problem());
                Assert.True(seen.TryAdd(key, info.Action), $"{key} is bound twice");
            }
        }

        Assert.Equal(Enum.GetValues<ShortcutAction>().Length, ShortcutCatalog.All.Count);
    }

    [Fact]
    public void Defaults_KeepTheKeysTheAppAlwaysHad()
    {
        var map = ShortcutMap.Defaults;
        Assert.Equal(ShortcutAction.BoxTool, map.Find(Key("B")));
        Assert.Equal(ShortcutAction.SelectTool, map.Find(Key("V")));
        Assert.Equal(ShortcutAction.SelectTool, map.Find(Key("S")));
        Assert.Equal(ShortcutAction.NextTool, map.Find(Key("Q")));
        Assert.Equal(ShortcutAction.PreviousTool, map.Find(Key("Shift+Q")));
        Assert.Equal(ShortcutAction.Redo, map.Find(Key("Ctrl+Y")));
        Assert.Equal(ShortcutAction.Redo, map.Find(Key("Ctrl+Shift+Z")));
        Assert.Equal(ShortcutAction.Undo, map.Find(Key("Ctrl+Z")));
        Assert.Equal(ShortcutAction.PreviousImage, map.Find(Key("Left")));
        Assert.Equal(ShortcutAction.Escape, map.Find(Key("Escape")));
    }

    [Fact]
    public void Find_IgnoresShiftOnlyWhenNoShiftedKeyIsBound()
    {
        // "+" is Shift+OemPlus on a US layout and must still zoom.
        Assert.Equal(ShortcutAction.ZoomIn, ShortcutMap.Defaults.Find(Key("Shift+OemPlus")));
        // Shift+Q is bound itself and must not fall through to Q.
        Assert.Equal(ShortcutAction.PreviousTool, ShortcutMap.Defaults.Find(Key("Shift+Q")));
        // Ctrl combinations are exact.
        Assert.Null(ShortcutMap.Defaults.Find(Key("Ctrl+Shift+Y")));
        Assert.Null(ShortcutMap.Defaults.Find(Key("X")));
    }

    [Fact]
    public void With_ReplacesTheKeysAndFreesTheOldOnes()
    {
        var map = ShortcutMap.Defaults.With(ShortcutAction.BoxTool, new[] { Key("X") });

        Assert.Equal(ShortcutAction.BoxTool, map.Find(Key("X")));
        Assert.Null(map.Find(Key("B")));
        Assert.True(map.IsCustom(ShortcutAction.BoxTool));
        Assert.False(ShortcutMap.Defaults.IsCustom(ShortcutAction.BoxTool));
    }

    [Fact]
    public void With_RefusesAKeyAnotherActionHoldsAndNamesIt()
    {
        var exception = Assert.Throws<ShortcutException>(() =>
            ShortcutMap.Defaults.With(ShortcutAction.BoxTool, new[] { Key("P") }));

        Assert.Contains("Công cụ Polygon", exception.Message);
    }

    [Theory]
    [InlineData("D1")]
    [InlineData("NumPad3")]
    [InlineData("LeftCtrl")]
    public void With_RefusesKeysThatAreReserved(string text)
    {
        var shortcut = new Shortcut(text);
        Assert.NotNull(shortcut.Problem());
        Assert.Throws<ShortcutException>(() => ShortcutMap.Defaults.With(ShortcutAction.BoxTool, new[] { shortcut }));
    }

    [Fact]
    public void Space_CanBeBoundLikeAnyOtherKey()
    {
        var map = ShortcutMap.Defaults.With(ShortcutAction.Finish, new[] { Key("Space") });

        Assert.Null(Key("Space").Problem());
        Assert.Equal(ShortcutAction.Finish, map.Find(Key("Space")));
        Assert.Equal("Space", Key("Space").Display);
    }

    [Fact]
    public void With_BackToTheDefaultsDropsTheOverride()
    {
        var changed = ShortcutMap.Defaults.With(ShortcutAction.BoxTool, new[] { Key("X") });
        var back = changed.With(ShortcutAction.BoxTool, new[] { Key("B") });

        Assert.False(back.IsCustom(ShortcutAction.BoxTool));
        Assert.False(back.HasCustom);
    }

    [Fact]
    public void ASwapIsPossibleInThreeSteps()
    {
        var map = ShortcutMap.Defaults
            .With(ShortcutAction.BoxTool, new[] { Key("X") })
            .With(ShortcutAction.PolygonTool, new[] { Key("B") })
            .With(ShortcutAction.BoxTool, new[] { Key("P") });

        Assert.Equal(ShortcutAction.PolygonTool, map.Find(Key("B")));
        Assert.Equal(ShortcutAction.BoxTool, map.Find(Key("P")));
    }

    [Fact]
    public void Display_UsesFriendlyNames()
    {
        Assert.Equal("Ctrl+Shift+Z", Key("Ctrl+Shift+Z").Display);
        Assert.Equal("Backspace", Key("Back").Display);
        Assert.Equal("Esc", Key("Escape").Display);
        Assert.Equal("+", Key("OemPlus").Display);
        Assert.Equal("←", Key("Left").Display);
        Assert.Equal("Shift+Q", Key("Shift+Q").Display);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("Shift")]
    [InlineData("A+B")]
    public void TryParse_RejectsNonsense(string text) => Assert.False(Shortcut.TryParse(text, out _));

    [Fact]
    public void Stored_RoundTripsOnlyWhatChanged()
    {
        var map = ShortcutMap.Defaults.With(ShortcutAction.BoxTool, new[] { Key("Ctrl+Shift+X") });

        var stored = map.ToStored();
        Assert.Single(stored);
        Assert.Equal(new[] { "Ctrl+Shift+X" }, stored["BoxTool"]);

        var restored = ShortcutMap.FromStored(stored);
        Assert.Equal(ShortcutAction.BoxTool, restored.Find(Key("Ctrl+Shift+X")));
    }

    [Fact]
    public void FromStored_IgnoresUnknownActionsAndBrokenKeys()
    {
        var map = ShortcutMap.FromStored(new Dictionary<string, IReadOnlyList<string>>
        {
            ["NoSuchAction"] = new[] { "X" },
            ["BoxTool"] = new[] { "Ctrl+" },
            ["PolygonTool"] = Array.Empty<string>(),
            ["TagTool"] = new[] { "D5" }
        });

        Assert.False(map.HasCustom);
        Assert.Equal(ShortcutAction.BoxTool, map.Find(Key("B")));
    }

    [Fact]
    public void FromStored_ResolvesAClashByRevertingTheLaterAction()
    {
        // A file edited by hand gives B to two actions: Box keeps it (its default), Fit loses its override.
        var map = ShortcutMap.FromStored(new Dictionary<string, IReadOnlyList<string>>
        {
            ["FitView"] = new[] { "B" }
        });

        Assert.Equal(ShortcutAction.BoxTool, map.Find(Key("B")));
        Assert.Equal(ShortcutAction.FitView, map.Find(Key("F")));
        Assert.False(map.HasCustom);
    }

    [Fact]
    public void Service_SavesEachChangeAndANewRunReadsItBack()
    {
        var service = NewService();
        var raised = 0;
        service.Changed += (_, _) => raised++;

        Assert.Null(service.Assign(ShortcutAction.BoxTool, Key("X")));
        Assert.Equal(1, raised);
        Assert.True(File.Exists(SettingsPath));

        var second = NewService();
        Assert.Equal(ShortcutAction.BoxTool, second.Shortcuts.Find(Key("X")));
        Assert.Null(second.Problem);
    }

    [Fact]
    public void Service_ReportsAConflictWithoutChangingAnything()
    {
        var service = NewService();
        var raised = 0;
        service.Changed += (_, _) => raised++;

        var problem = service.Assign(ShortcutAction.BoxTool, Key("P"));

        Assert.NotNull(problem);
        Assert.Equal(0, raised);
        Assert.False(service.Shortcuts.HasCustom);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void Service_ResetBringsBackTheDefaults()
    {
        var service = NewService();
        service.Assign(ShortcutAction.BoxTool, Key("X"));
        service.Assign(ShortcutAction.PolygonTool, Key("Y"));

        service.Reset(ShortcutAction.BoxTool);
        Assert.Equal(ShortcutAction.BoxTool, service.Shortcuts.Find(Key("B")));
        Assert.Equal(ShortcutAction.PolygonTool, service.Shortcuts.Find(Key("Y")));

        service.ResetAll();
        Assert.False(service.Shortcuts.HasCustom);
        Assert.False(NewService().Shortcuts.HasCustom);
    }

    [Fact]
    public void Service_ACorruptFileFallsBackToDefaultsAndSaysSo()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var service = NewService();

        Assert.False(service.Shortcuts.HasCustom);
        Assert.Contains("mặc định", service.Problem);
    }

    [Fact]
    public void Service_AnUnwritableLocationKeepsTheChangeForThisRunAndSaysSo()
    {
        // The settings path's parent is a file, so the folder cannot be created.
        var blocker = Path.Combine(_temp.Path, "blocker");
        File.WriteAllText(blocker, "x");
        var service = new SettingsService(new JsonSettingsStore(Path.Combine(blocker, "settings.json")));

        Assert.Null(service.Assign(ShortcutAction.BoxTool, Key("X")));

        Assert.Equal(ShortcutAction.BoxTool, service.Shortcuts.Find(Key("X")));
        Assert.Contains("lần chạy này", service.Problem);
    }
}
