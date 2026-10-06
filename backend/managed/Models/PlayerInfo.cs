// Derived from wuyan1337/yySync; adapted for BetterNCM on 2026-10-06. See NOTICE.md and LICENSE.
namespace YySyncNcm.Models;
internal readonly record struct PlayerInfo
{
    public required string Title { get; init; }
    public required string Artists { get; init; }
    public required double Schedule { get; init; }
    public required double Duration { get; init; }
    public required bool Pause { get; init; }
}
