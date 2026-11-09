namespace PassKeeper.Core.AutoType;

/// <summary>One automatic sign-in: it stops counting against the limit once it turned out to be successful.</summary>
public sealed class SignInAttempt
{
    internal SignInAttempt(DateTime at) => At = at;

    public DateTime At { get; }

    /// <summary>When the sign-in form was seen to have gone after this attempt.</summary>
    public DateTime? SucceededAt { get; internal set; }

    /// <summary>The form asked again after this attempt: it can no longer turn out to be successful.</summary>
    public bool Refused { get; internal set; }

    /// <summary>Returns false when the form has already asked again, so the attempt keeps counting.</summary>
    public bool MarkSucceeded(DateTime now)
    {
        if (Refused) return false;
        SucceededAt = now;
        return true;
    }
}

/// <summary>
/// The limit on automatic sign-ins of an entry, so that a wrong saved password cannot lock the account: only attempts
/// that did not turn out to be successful count. A form that asks again right after a "success" (within
/// <paramref name="quickRetry"/> of it) shows the password was refused after all: a slow refusal looks like a success
/// at first, and such an attempt counts.
/// </summary>
public sealed class SignInAttempts(int max, TimeSpan window, TimeSpan quickRetry)
{
    private readonly Dictionary<Guid, List<SignInAttempt>> _byEntry = [];

    public bool Allowed(Guid entry, DateTime now) => Count(entry, now) < max;

    /// <summary>Attempts that count against the limit now.</summary>
    public int Count(Guid entry, DateTime now)
    {
        if (!_byEntry.TryGetValue(entry, out var attempts)) return 0;
        var last = attempts.Count > 0 ? attempts[^1] : null;
        return attempts.Count(a => now - a.At < window && (a.SucceededAt == null || a == last && now - a.SucceededAt < quickRetry));
    }

    /// <summary>
    /// The form is asking for the password again: the last attempt was refused, even if its window is gone and a watch
    /// is still waiting to confirm it. A success confirmed longer than <c>quickRetry</c> ago stands: this is a new sign-in.
    /// </summary>
    public void AskedAgain(Guid entry, DateTime now)
    {
        if (!_byEntry.TryGetValue(entry, out var attempts) || attempts.Count == 0) return;
        var last = attempts[^1];
        if (now - last.SucceededAt >= quickRetry) return;
        last.SucceededAt = null;
        last.Refused = true;
    }

    /// <summary>Notes an attempt being made.</summary>
    public SignInAttempt Record(Guid entry, DateTime now)
    {
        AskedAgain(entry, now);
        if (!_byEntry.TryGetValue(entry, out var attempts)) _byEntry[entry] = attempts = [];
        attempts.RemoveAll(a => now - a.At >= window);
        var attempt = new SignInAttempt(now);
        attempts.Add(attempt);
        return attempt;
    }
}
