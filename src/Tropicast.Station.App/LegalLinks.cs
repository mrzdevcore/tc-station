namespace Tropicast.Station.App;

/// <summary>The Terms of Service and Privacy Policy pages of the Tropicast web app the API belongs to (tc-dashboard#94).</summary>
public sealed record LegalLinks(Uri TermsOfService, Uri PrivacyPolicy)
{
    public static LegalLinks For(Uri webApp) => new(new Uri(webApp, "/terms"), new Uri(webApp, "/privacy"));
}
