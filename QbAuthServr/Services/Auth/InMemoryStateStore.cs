using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace QbAuthServr.Services.Auth;

public sealed class InMemoryStateStore : IStateStore
{
    private readonly ConcurrentDictionary<string, byte> _store = new(StringComparer.Ordinal);

    public string Create()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        _store[state] = 1;
        return state;
    }

    public bool ValidateAndConsume(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        return _store.TryRemove(state, out _);
    }
}