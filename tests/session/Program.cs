using MusicRpc;
using SteamKit2;

foreach (var transient in new[] { EResult.NoConnection, EResult.Timeout, EResult.ServiceUnavailable, EResult.TryAnotherCM })
    if (SteamSessionManager.InvalidatesToken(transient))
        throw new Exception($"Transient failure must retain token: {transient}");
foreach (var rejected in new[] { EResult.InvalidPassword, EResult.Expired, EResult.Revoked })
    if (!SteamSessionManager.InvalidatesToken(rejected))
        throw new Exception($"Revoked credential must require login: {rejected}");
var path = Environment.GetEnvironmentVariable("YYSYNC_CONFIG_DIRECTORY") ?? throw new Exception("Use a temporary test config");
if (args.Length > 0 && args[0] == "write")
{
    var settings = Configurations.Instance.Settings;
    settings.SteamUsername = "restart-test";
    settings.SteamRefreshToken = "restart-token";
    settings.SteamGuardData = "restart-guard";
    Configurations.Instance.Save();
}
else
{
    var settings = Configurations.Instance.Settings;
    if (settings.SteamUsername != "restart-test" || settings.SteamRefreshToken != "restart-token" ||
        settings.SteamGuardData != "restart-guard") throw new Exception("Credentials did not survive process restart");
}
Console.WriteLine("Token rejection policy and process-restart credential persistence passed");
