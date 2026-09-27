using System.Collections.Concurrent;
using InstituteMS.Application.Chat;

namespace InstituteMS.Api.Hubs;

/// <summary>
/// In-memory, per-process presence. A singleton rather than anything backed by
/// the database — see the interface's own remarks for why that is the right
/// call for this particular piece of state.
/// </summary>
/// <remarks>
/// Tracked per connection, not per person: someone with a desktop tab open and
/// a phone in their pocket is away only when EVERY one of their connections is
/// away. One flag per person let whichever device spoke last win, so a phone
/// going to sleep marked someone away while they sat at their desk, and closing
/// that phone never cleared it. Emails are held lower-cased so the same person
/// is one entry however their address happens to be capitalised.
/// </remarks>
public class ChatPresenceTracker : IChatPresenceTracker
{
    private readonly object _gate = new();

    /// <summary>email → (connectionId → that connection is away).</summary>
    private readonly Dictionary<string, Dictionary<string, bool>> _connections = new();

    /// <summary>Only ever holds someone also present in <see cref="_connections"/>.</summary>
    private readonly ConcurrentDictionary<string, bool> _inCallByEmail = new();

    private static string Key(string email) => email.Trim().ToLowerInvariant();

    private bool IsAwayLocked(string key) =>
        _connections.TryGetValue(key, out var conns) && conns.Count > 0 && conns.Values.All(away => away);

    public bool MarkConnected(string email, string connectionId, out bool awayChanged)
    {
        var key = Key(email);
        lock (_gate)
        {
            var wasAway = IsAwayLocked(key);
            if (!_connections.TryGetValue(key, out var conns))
                _connections[key] = conns = new Dictionary<string, bool>();

            var first = conns.Count == 0;
            // A connection that has just opened is someone actively using the app.
            conns[connectionId] = false;

            awayChanged = !first && wasAway;
            return first;
        }
    }

    public bool MarkDisconnected(string email, string connectionId, out bool awayChanged)
    {
        var key = Key(email);
        lock (_gate)
        {
            awayChanged = false;
            if (!_connections.TryGetValue(key, out var conns)) return false;

            var wasAway = IsAwayLocked(key);
            conns.Remove(connectionId);

            if (conns.Count == 0)
            {
                _connections.Remove(key);
                _inCallByEmail.TryRemove(key, out _);
                return true;
            }

            awayChanged = wasAway != IsAwayLocked(key);
            return false;
        }
    }

    public bool SetAway(string email, string connectionId, bool away)
    {
        var key = Key(email);
        lock (_gate)
        {
            if (!_connections.TryGetValue(key, out var conns) || !conns.ContainsKey(connectionId)) return false;

            var wasAway = IsAwayLocked(key);
            conns[connectionId] = away;
            return wasAway != IsAwayLocked(key);
        }
    }

    public bool IsAway(string email)
    {
        lock (_gate) return IsAwayLocked(Key(email));
    }

    public bool SetInCall(string email, bool inCall)
    {
        var key = Key(email);
        lock (_gate)
        {
            if (!_connections.ContainsKey(key)) return false;
        }

        if (inCall)
        {
            var changed = true;
            _inCallByEmail.AddOrUpdate(key, _ => true, (_, was) => { changed = !was; return true; });
            return changed;
        }

        return _inCallByEmail.TryRemove(key, out _);
    }

    public IReadOnlyCollection<string> OnlineEmails
    {
        get { lock (_gate) return _connections.Keys.ToList(); }
    }

    public IReadOnlyCollection<string> AwayEmails
    {
        get { lock (_gate) return _connections.Keys.Where(IsAwayLocked).ToList(); }
    }

    public IReadOnlyCollection<string> InCallEmails => _inCallByEmail.Keys.ToList();
}
