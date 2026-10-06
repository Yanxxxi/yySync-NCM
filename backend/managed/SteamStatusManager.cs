// Derived from wuyan1337/yySync; adapted for BetterNCM on 2026-10-06. See NOTICE.md and LICENSE.
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using YySyncNcm.Models;
namespace YySyncNcm;
internal sealed class SteamStatusManager
{
    private readonly SteamSessionManager _session;
    private string _lastSetName = string.Empty;
    private int _lastGeneration = -1;
    private const int ProgressBarLength = 10;
    public SteamStatusManager(SteamSessionManager session)
    {
        _session = session;
    }
    public async Task UpdateStatusAsync(PlayerInfo info)
    {
        if (!_session.IsLoggedOn) return;
        var config = Configurations.Instance.Settings;
        var newName = GetStatusPreview(info, config);
        if (newName == _lastSetName && _lastGeneration == _session.SessionGeneration) return;
        try
        {
            await _session.SetGameNameAsync(newName).ConfigureAwait(false);
            _lastSetName = newName;
            _lastGeneration = _session.SessionGeneration;
            Debug.WriteLine($"[SteamStatus] 状态已更新: {newName}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SteamStatus] 更新状态失败: {ex.Message}");
        }
    }
    public void ClearStatus()
    {
        if (!_session.IsLoggedOn) return;
        _session.ClearGameName();
        _lastSetName = string.Empty;
        Debug.WriteLine("[SteamStatus] 状态已清除");
    }
    private static int GetUtf8ByteCount(string str)
    {
        return System.Text.Encoding.UTF8.GetByteCount(str);
    }
    private static string TruncateToUtf8ByteLength(string str, int maxBytes)
    {
        if (string.IsNullOrEmpty(str)) return str;
        var bytes = System.Text.Encoding.UTF8.GetBytes(str);
        if (bytes.Length <= maxBytes) return str;
        int byteCount = 0;
        int charCount = 0;
        foreach (var rune in str.EnumerateRunes())
        {
            int cBytes = rune.Utf8SequenceLength;
            if (byteCount + cBytes > maxBytes) break;
            byteCount += cBytes;
            charCount += rune.Utf16SequenceLength;
        }
        return str.Substring(0, charCount);
    }
    public static string GetStatusPreview(PlayerInfo playerInfo, ConfigData config)
    {
        var prefix = config.EnableCustomPrefix && !string.IsNullOrEmpty(config.CustomPrefix)
            ? config.CustomPrefix
            : string.Empty;
        var prefixBytes = GetUtf8ByteCount(prefix);
        var title = playerInfo.Title;
        var artistPart = string.Empty;
        var progressPart = string.Empty;
        if (config.ShowArtistName && !string.IsNullOrEmpty(playerInfo.Artists))
        {
            artistPart = $" - {playerInfo.Artists}";
        }
        if (config.ShowProgressBar && !playerInfo.Pause && playerInfo.Duration > 0)
        {
            var sbStats = new System.Text.StringBuilder();
            sbStats.Append(" [");
            var progress = Math.Clamp(playerInfo.Schedule / playerInfo.Duration, 0, 1);
            var filledCount = (int)(progress * ProgressBarLength);
            sbStats.Append(new string('#', filledCount));
            sbStats.Append(new string('-', ProgressBarLength - filledCount));
            sbStats.Append($"] {FormatTime(playerInfo.Schedule)}/{FormatTime(playerInfo.Duration)}");
            progressPart = sbStats.ToString();
        }
        else if (playerInfo.Pause)
        {
            progressPart = " (Paused)";
        }
        var contentMaxBytes = 63 - prefixBytes;
        if (contentMaxBytes <= 0) return TruncateToUtf8ByteLength(prefix, 63);
        var fullString = $"{title}{artistPart}{progressPart}";
        if (GetUtf8ByteCount(fullString) <= contentMaxBytes) return $"{prefix}{fullString}";
        if (config.StatusPriority == SteamStatusPriority.Artist)
        {
            var artistString = $"{title}{artistPart}";
            if (GetUtf8ByteCount(artistString) <= contentMaxBytes) return $"{prefix}{artistString}";
            return $"{prefix}{TruncateToUtf8ByteLength(title, contentMaxBytes)}";
        }
        else
        {
            var progressString = $"{title}{progressPart}";
            if (GetUtf8ByteCount(progressString) <= contentMaxBytes) return $"{prefix}{progressString}";
            return $"{prefix}{TruncateToUtf8ByteLength(title, contentMaxBytes)}";
        }
    }
    private static string FormatTime(double totalSeconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, totalSeconds));
        return ts.Hours > 0
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }
}
