using System.Text.Json;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public class WhiteListService
{
    private static readonly string FilePath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "whitelist.json");

    private List<WhiteItem> _items = new();
    private HashSet<string> _shaLookup = new(StringComparer.OrdinalIgnoreCase);

    public WhiteListService()
    {
        Load();
    }

    public bool IsWhitelisted(string sha256)
    {
        return _shaLookup.Contains(sha256);
    }

    public void Add(string fileName, string sha256)
    {
        if (_shaLookup.Contains(sha256)) return;

        _items.Add(new WhiteItem
        {
            FileName = fileName,
            Sha256 = sha256,
            AddedAt = DateTime.Now,
            Source = "manual"
        });

        _shaLookup.Add(sha256);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;

            var json = File.ReadAllText(FilePath);
            _items = JsonSerializer.Deserialize<List<WhiteItem>>(json) ?? new();

            _shaLookup = new HashSet<string>(_items.Select(x => x.Sha256), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Load 정지: {ex.Message}");
            _items = new();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Save 실패: {ex.Message}");
        }
    }
}

public class WhiteItem
{
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public string Source { get; set; } = "";
}