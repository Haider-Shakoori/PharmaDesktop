using BusinessOS.Pharmacy.Application.Abstractions.Persistence;

namespace BusinessOS.Pharmacy.Desktop.Backup;

/// <summary>Settings are tenant-local and do not contain credentials or backup payloads.</summary>
public sealed record AutomaticBackupPolicy(bool Enabled, int IntervalHours, int KeepCount)
{
    public const string EnabledKey = "backup.auto.enabled";
    public const string IntervalKey = "backup.auto.interval_hours";
    public const string KeepKey = "backup.auto.keep_count";
    public static readonly int[] Intervals = [1, 6, 24];
    public static readonly int[] KeepOptions = [7, 14, 30];

    public static async Task<AutomaticBackupPolicy> ReadAsync(
        ILocalSettingsStore settings, CancellationToken token = default)
    {
        var enabled = await settings.GetAsync(EnabledKey, token) == "true";
        var intervalText = await settings.GetAsync(IntervalKey, token);
        var keepText = await settings.GetAsync(KeepKey, token);
        var interval = int.TryParse(intervalText, out var hours) && Intervals.Contains(hours)
            ? hours : 24;
        var keep = int.TryParse(keepText, out var count) && KeepOptions.Contains(count)
            ? count : 14;
        return new(enabled, interval, keep);
    }

    public async Task WriteAsync(ILocalSettingsStore settings, CancellationToken token = default)
    {
        if (!Intervals.Contains(IntervalHours) || !KeepOptions.Contains(KeepCount))
            throw new ArgumentException("Unsupported automatic backup schedule or retention.");
        await settings.SetAsync(EnabledKey, Enabled ? "true" : "false", token);
        await settings.SetAsync(IntervalKey, IntervalHours.ToString(), token);
        await settings.SetAsync(KeepKey, KeepCount.ToString(), token);
    }
}
