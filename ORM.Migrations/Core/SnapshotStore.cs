using System.Text.Json;

namespace ORM.Migrations.Core;

public static class SnapshotStore
{
    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true
    };

    public static SchemaSnapshot? Load(string path)
    {
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SchemaSnapshot>(json, Opt);
    }

    public static void Save(string path, SchemaSnapshot snapshot)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(snapshot, Opt);
        File.WriteAllText(path, json);
    }
}