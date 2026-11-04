using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using MuSync.Models;
using MuSync.Utils;
using Xunit;

namespace MuSync.Tests;

[Collection(LanguageSensitiveCollection.Name)]
public class ProgramLibraryTests
{
    [Fact]
    public void LibrarySearchCombinesNameExeAndCategoryWithoutIgnoredEntries()
    {
        Assert.NotEmpty(ProgramLibrary.Entries);
        Assert.DoesNotContain(ProgramLibrary.Entries, entry => AppClassifier.IsBuiltInIgnored(entry.ExeName));
        Assert.Equal(ProgramLibrary.Entries.Count,
            ProgramLibrary.Entries.Select(entry => entry.ExeName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(ProgramLibrary.Search(" CODE.EXE ", AppCategory.Work), entry => entry.ExeName == "code.exe");
        Assert.Empty(ProgramLibrary.Search("code.exe", AppCategory.Game));
        Assert.Contains(ProgramLibrary.Search("原神", null), entry => entry.ExeName == "yuanshen.exe");
        Assert.Empty(ProgramLibrary.Search("no-such-app-6e12a67b", null));
    }

    [Fact]
    public void AddingLibraryEntryNeverOverwritesAnExistingCustomRule()
    {
        var custom = new AppRule { ExeName = "CODE.EXE", DisplayName = "My editor", Enabled = false, Override = AppSyncOverride.ForceOff };
        var rules = new List<AppRule> { custom };
        var entry = ProgramLibrary.Search("code.exe", null).Single();
        Assert.False(ProgramLibrary.TryAdd(rules, entry));
        Assert.Same(custom, Assert.Single(rules));
        Assert.Equal("My editor", custom.DisplayName);
        Assert.False(custom.Enabled);
        Assert.Equal(AppSyncOverride.ForceOff, custom.Override);
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("MuSync.exe")]
    [InlineData("cloudmusic.exe")]
    [InlineData("../something.exe")]
    [InlineData("C:\\something.exe")]
    [InlineData("something.txt")]
    [InlineData(".exe")]
    public void IneffectiveOrInvalidRulesAreNotAdded(string exe)
    {
        var rules = new List<AppRule>();
        Assert.False(ProgramLibrary.TryAdd(rules, new ProgramLibrary.Entry(exe, "test", AppCategory.Other)));
        Assert.Empty(rules);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InlineLibraryAddsOnlyToDraftAndReloadDiscardsIt(int language)
    {
        var config = Configurations.Instance.Settings;
        var oldLanguage = config.Language;
        var previous = config.Apps;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            config.Language = language;
            config.Apps = [];
            using var view = new SettingsView();
            var type = typeof(SettingsView);
            T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(view)!;
            var grid = Field<DataGridView>("_programLibraryGrid");
            var panel = Field<Panel>("_programLibraryPanel");
            var page = (TabPage)panel.Parent!;
            type.GetMethod("ShowProgramLibrary", flags)!.Invoke(view, [page, true]);
            var search = Field<TextBox>("_programLibrarySearch");
            search.Text = "code.exe";
            Assert.Single(grid.Rows.Cast<DataGridViewRow>());
            var add = Field<Button>("_programLibraryAdd");
            Assert.True(add.Enabled);
            type.GetMethod("AddSelectedLibraryEntry", flags)!.Invoke(view, null);
            var rules = Field<List<AppRule>>("_rules");
            var added = Assert.Single(rules);
            Assert.Equal("code.exe", added.ExeName);
            Assert.True(added.IsUserConfirmed && added.Enabled);
            Assert.Equal(AppSyncMode.Foreground, added.Mode);
            Assert.False(add.Enabled);
            Assert.Empty(config.Apps);
            type.GetMethod("LoadSettings", flags)!.Invoke(view, null);
            Assert.Empty(rules);
            Assert.Empty(config.Apps);
            type.GetMethod("ShowProgramLibrary", flags)!.Invoke(view, [page, true]);
            Assert.True(add.Enabled);
            search.Text = "no-such-app-6e12a67b";
            Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
            Assert.False(add.Enabled);
        }
        finally { config.Language = oldLanguage; config.Apps = previous; }
    }
}
