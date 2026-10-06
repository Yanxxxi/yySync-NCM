using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MusicRpc.Models;
using MusicRpc.Utils;

namespace MusicRpc;

internal sealed class BetterNcmBridge(string stateFile, SteamStatusManager steamManager)
{
    private sealed class Snapshot
    {
        public DateTimeOffset UpdatedAt { get; set; }
        public Song? Song { get; set; }
        public bool Paused { get; set; }
        public double CurrentTimeMs { get; set; }
        public double DurationMs { get; set; }
    }

    private sealed class Song
    {
        public string? Id { get; set; }
        public string? Title { get; set; }
        public string? Artists { get; set; }
        public string? Album { get; set; }
        public string? Cover { get; set; }
    }

    private bool _hasStatus;
    private DateTimeOffset _lastFreshSnapshot = DateTimeOffset.UtcNow;

    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var snapshot = JsonSerializer.Deserialize<Snapshot>(
                    File.ReadAllText(stateFile),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var age = DateTimeOffset.UtcNow - snapshot?.UpdatedAt;
                if (age >= TimeSpan.FromSeconds(-5) && age < TimeSpan.FromSeconds(5))
                    _lastFreshSnapshot = DateTimeOffset.UtcNow;
                if (snapshot?.Song is { } song &&
                    age >= TimeSpan.FromSeconds(-5) && age < TimeSpan.FromSeconds(5) &&
                    !string.IsNullOrWhiteSpace(song.Title))
                {
                    var duration = CleanSeconds(snapshot.DurationMs);
                    var info = new PlayerInfo
                    {
                        Identity = Cut(song.Id, 128),
                        Title = Cut(song.Title, 512),
                        Artists = Cut(song.Artists, 512),
                        Album = Cut(song.Album, 512),
                        Cover = Cut(song.Cover, 2048),
                        Schedule = Math.Clamp(CleanSeconds(snapshot.CurrentTimeMs), 0, duration > 0 ? duration : double.MaxValue),
                        Duration = duration,
                        Pause = snapshot.Paused,
                        Url = ""
                    };
                    if (Configurations.Instance.Settings.EnableSteamSync)
                    {
                        await steamManager.UpdateStatusAsync(info, "网易云音乐");
                        _hasStatus = true;
                    }
                    else
                    {
                        ClearIfNeeded();
                    }
                }
                else
                {
                    ClearIfNeeded();
                }
            }
            catch (IOException)
            {
                ClearIfNeeded();
            }
            catch (JsonException)
            {
                // The plugin may be in the middle of rewriting the snapshot.
                if (File.Exists(stateFile) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(stateFile) > TimeSpan.FromSeconds(5))
                    ClearIfNeeded();
            }
            catch (Exception ex)
            {
                Logger.Error($"BetterNCM bridge error: {ex.Message}");
                ClearIfNeeded();
            }

            if (DateTimeOffset.UtcNow - _lastFreshSnapshot > TimeSpan.FromMinutes(1))
            {
                Application.Exit();
                break;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(1), token); }
            catch (OperationCanceledException) { break; }
        }

        ClearIfNeeded();
    }

    private void ClearIfNeeded()
    {
        if (!_hasStatus) return;
        steamManager.ClearStatus();
        _hasStatus = false;
    }

    private static double CleanSeconds(double milliseconds) =>
        double.IsFinite(milliseconds) && milliseconds > 0 ? milliseconds / 1000 : 0;

    private static string Cut(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) ? "" : value.Length > maxLength ? value[..maxLength] : value;
}
