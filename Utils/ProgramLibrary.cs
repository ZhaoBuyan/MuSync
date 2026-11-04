using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MuSync.Models;

namespace MuSync.Utils;

internal static class ProgramLibrary
{
    internal sealed record Entry(string ExeName, string Name, AppCategory Category);
    internal static IReadOnlyList<Entry> Entries { get; } = AppClassifier.GetLibraryEntries();

    internal static IEnumerable<Entry> Search(string? query, AppCategory? category)
    {
        var text = query?.Trim() ?? "";
        return Entries.Where(entry => (!category.HasValue || entry.Category == category) &&
            (entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
             entry.ExeName.Contains(text, StringComparison.OrdinalIgnoreCase)));
    }

    // Only a new draft rule is added; existing custom names, switches and strategies are untouched.
    internal static bool TryAdd(ICollection<AppRule> rules, Entry entry)
    {
        if (AppClassifier.IsBuiltInIgnored(entry.ExeName) ||
            !entry.ExeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            entry.ExeName.IndexOfAny(['/', '\\', ':']) >= 0 ||
            string.IsNullOrWhiteSpace(Path.GetFileNameWithoutExtension(entry.ExeName)) ||
            rules.Any(rule => rule.ExeName.Equals(entry.ExeName, StringComparison.OrdinalIgnoreCase)))
            return false;
        rules.Add(new AppRule
        {
            ExeName = entry.ExeName, DisplayName = entry.Name, Category = entry.Category,
            Enabled = true, IsUserConfirmed = true, Mode = AppSyncMode.Foreground
        });
        return true;
    }
}
