using PassKeeper.Core.Matching;

namespace PassKeeper.Core.AutoType;

public enum SignInOutcome
{
    /// <summary>The page says nothing about the sign-in yet.</summary>
    Unknown,
    Accepted,
    Refused,
}

/// <summary>
/// What a page shown in the browser window after PassKeeper sent a password there tells about the sign-in. The pages
/// are looked at anyway, for autofill, so nothing more is read from the browser for this.
/// </summary>
public static class SiteSignInCheck
{
    /// <summary>Pages shown later than this after the password are not taken as its answer.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    /// <summary>A page of another site this soon is the redirect after signing in (single sign-on to an application).</summary>
    public static readonly TimeSpan Redirect = TimeSpan.FromSeconds(20);

    /// <summary>
    /// A sign-in form seen sooner than this is still the page the password was typed on (the browser reports the
    /// cursor moving through it with a delay, the address may not show the new page yet).
    /// </summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);

    /// <param name="signedInUrl">The page the password was sent from.</param>
    /// <param name="since">Time since the password was sent.</param>
    /// <param name="url">The page shown now in the same window.</param>
    /// <param name="signInForm">Whether that page shows a sign-in form; null when it was not looked at.</param>
    public static SignInOutcome Judge(string signedInUrl, TimeSpan since, string? url, bool? signInForm)
    {
        if (since > Wait) return SignInOutcome.Unknown;
        var host = DomainUtil.GetHost(url);
        var signedInHost = DomainUtil.GetHost(signedInUrl);
        if (host == null || signedInHost == null) return SignInOutcome.Unknown;
        if (!string.Equals(host, signedInHost, StringComparison.OrdinalIgnoreCase))
            return since < Redirect ? SignInOutcome.Accepted : SignInOutcome.Unknown;
        return signInForm switch
        {
            false => SignInOutcome.Accepted,
            // The page the password was typed on, still shown or reported late, is no answer yet.
            true => since < Settle || url == signedInUrl ? SignInOutcome.Unknown : SignInOutcome.Refused,
            null => SignInOutcome.Unknown,
        };
    }
}
