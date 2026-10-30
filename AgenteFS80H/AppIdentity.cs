namespace Futureplace.AgenteFS80H.Desktop;

internal static class AppIdentity
{
    public const string Name = "Central Biométrica";
    // One version source for the UI, Windows metadata, installer and release manifest.
    public static string Version => typeof(AppIdentity).Assembly.GetName().Version!.ToString(3);
}
