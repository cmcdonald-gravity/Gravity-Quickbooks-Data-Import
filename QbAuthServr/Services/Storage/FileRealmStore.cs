using System.Text.Json;
using QbAuthServr.Models;

namespace QbAuthServr.Services.Storage;

public sealed class FileRealmStore : ITokenStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public FileRealmStore(string path)
    {
        _path = path;
        if (!File.Exists(_path)) File.WriteAllText(_path, "{}");
    }

    public Task SaveAsync(string? realmId, RealmAuth data)
    {
        realmId ??= "";
        lock (_lock)
        {
            var json = File.ReadAllText(_path);
            Dictionary<string, RealmAuth>? map = null;
            try { map = JsonSerializer.Deserialize<Dictionary<string, RealmAuth>>(json); } catch { }
            map ??= new Dictionary<string, RealmAuth>(StringComparer.OrdinalIgnoreCase);
            map[realmId] = data;

            File.WriteAllText(_path, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
        }
        return Task.CompletedTask;
    }

    public Task<RealmAuth?> GetAsync(string? realmId)
    {
        realmId ??= "";
        lock (_lock)
        {
            var json = File.ReadAllText(_path);
            Dictionary<string, RealmAuth>? map = null;
            try { map = JsonSerializer.Deserialize<Dictionary<string, RealmAuth>>(json); } catch { }
            map ??= new Dictionary<string, RealmAuth>(StringComparer.OrdinalIgnoreCase);

            map.TryGetValue(realmId, out var auth);
            return Task.FromResult(auth);
        }
    }
}