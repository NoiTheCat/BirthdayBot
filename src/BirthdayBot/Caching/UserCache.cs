using System.Collections.Concurrent;

namespace WorldTime.Caching;

public sealed class UserCache {
    private readonly ConcurrentDictionary<ulong, ConcurrentDictionary<ulong, UserInfo>> _cache = new();

    public int GuildsCount => _cache.Count;
    public int UsersCount => _cache.Values.Sum(v => v.Count);

    public void Update(UserInfo info) {
        var guild = _cache.GetOrAdd(info.GuildId, _ => new());
        guild[info.UserId] = info;
    }

    /// <summary>
    /// Gets all valid, non-expired, and optionally null entries in cache corresponding to the given guild.
    /// </summary>
    public IEnumerable<UserInfo> GetEntriesForGuild(ulong guildId, bool includeNullEntries) {
        if (_cache.TryGetValue(guildId, out var uinfos)) {
            var now = DateTimeOffset.UtcNow;
            foreach (var (_, entry) in uinfos) {
                if (!includeNullEntries && entry.IsNull) continue;
                if (now > entry.EntryTTL) continue;
                yield return entry;
            }
        }
        yield break;
    }

    /// <summary>
    /// Returns a non-concurrent dictionary holding a shallow copy of what the cache knows about the given guild.
    /// Excludes null and expired users.
    /// </summary>
    /// <remarks>Intended for use by listing command handlers.</remarks>
    public Dictionary<ulong, UserInfo>? GetGuildCopy(ulong guildId) {
        if (!_cache.TryGetValue(guildId, out var source)) return null;

        var result = new Dictionary<ulong, UserInfo>();
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, entry) in source) {
            if (entry.IsNull) continue;
            if (now > entry.EntryTTL) continue;
            result.Add(id, entry); // a shallow copy is completely fine
        }
        if (result.Count == 0) return null; // should ideally not happen, but just in case
        return result;
    }

    public void Sweep() {
        foreach (var (id, _) in _cache) {
            Sweep(id);
        }
    }

    public void Sweep(ulong guildId) {
        if (!_cache.TryGetValue(guildId, out var guild)) return;
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, entry) in guild) {
            if (now > entry.EntryTTL)
                guild.TryRemove(id, out _);
        }
        if (guild.IsEmpty) _cache.TryRemove(guildId, out _);
    }
}
