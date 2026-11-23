using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Zugapp;

internal sealed record HistoryEntry(DateTime Time, string App, string RawText, string FinalText);

internal sealed class History
{
    private const int MaxEntries = 200;
    private readonly List<HistoryEntry> _entries;

    public IReadOnlyList<HistoryEntry> Entries => _entries;

    private History(List<HistoryEntry> entries) => _entries = entries;

    public static History Load()
    {
        try
        {
            if (File.Exists(AppPaths.HistoryFile))
                return new History(JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(AppPaths.HistoryFile)) ?? new());
        }
        catch (Exception ex)
        {
            Log.Error("Verlauf konnte nicht geladen werden", ex);
        }
        return new History(new());
    }

    public void Add(HistoryEntry entry, bool persist)
    {
        _entries.Insert(0, entry);
        if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        if (persist) Save();
    }

    public void Clear()
    {
        _entries.Clear();
        Save();
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(AppPaths.HistoryFile, JsonSerializer.Serialize(_entries));
        }
        catch (Exception ex)
        {
            Log.Error("Verlauf konnte nicht gespeichert werden", ex);
        }
    }
}
