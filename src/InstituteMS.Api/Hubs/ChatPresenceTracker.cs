using System.Collections.Concurrent;
using InstituteMS.Application.Chat;

namespace InstituteMS.Api.Hubs;

/// <summary>
/// In-memory, per-process presence. A singleton rather than anything backed by
/// the database — see the interface's own remarks for why that is the right
/// call for this particular piece of state.
/// </summary>
public class ChatPresenceTracker : IChatPresenceTracker
{
    private readonly ConcurrentDictionary<string, int> _connectionsByEmail = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Only ever holds an entry for someone also present in <see cref="_connectionsByEmail"/>.</summary>
    private readonly ConcurrentDictionary<string, bool> _awayByEmail = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Only ever holds an entry for someone also present in <see cref="_connectionsByEmail"/>.</summary>
    private readonly ConcurrentDictionary<string, bool> _inCallByEmail = new(StringComparer.OrdinalIgnoreCase);

    public bool MarkConnected(string email)
    {
        var newCount = _connectionsByEmail.AddOrUpdate(email, 1, (_, count) => count + 1);
        return newCount == 1;
    }

    public bool MarkDisconnected(string email)
    {
        // Decrement, then drop the entry once it reaches zero so OnlineEmails
        // (a live Keys snapshot) never carries stale zero-count residents.
        var newCount = _connectionsByEmail.AddOrUpdate(email, 0, (_, count) => Math.Max(0, count - 1));

        if (newCount <= 0)
        {
            _connectionsByEmail.TryRemove(email, out _);
            _awayByEmail.TryRemove(email, out _);
            _inCallByEmail.TryRemove(email, out _);
            return true;
        }

        return false;
    }

    public bool SetAway(string email, bool away)
    {
        if (!_connectionsByEmail.ContainsKey(email)) return false;

        if (away)
        {
            // AddOrUpdate rather than a plain set, so "changed" is judged
            // against whatever the previous value actually was.
            var changed = true;
            _awayByEmail.AddOrUpdate(email, _ => true, (_, was) => { changed = !was; return true; });
            return changed;
        }

        return _awayByEmail.TryRemove(email, out _);
    }

    public bool SetInCall(string email, bool inCall)
    {
        if (!_connectionsByEmail.ContainsKey(email)) return false;

        if (inCall)
        {
            var changed = true;
            _inCallByEmail.AddOrUpdate(email, _ => true, (_, was) => { changed = !was; return true; });
            return changed;
        }

        return _inCallByEmail.TryRemove(email, out _);
    }

    public IReadOnlyCollection<string> OnlineEmails => _connectionsByEmail.Keys.ToList();

    public IReadOnlyCollection<string> AwayEmails => _awayByEmail.Keys.ToList();

    public IReadOnlyCollection<string> InCallEmails => _inCallByEmail.Keys.ToList();
}
