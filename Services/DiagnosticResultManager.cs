using System;
using System.Collections.Generic;
using System.Linq;

namespace ZapretMirrlyGUI.Services;

public class DiagnosticPresetScore
{
    public string PresetName { get; set; } = "";
    public int SuccessCount { get; set; }
    public int TotalCount { get; set; }
    public long AvgPingMs { get; set; }
    public double SuccessRate => TotalCount > 0 ? ((double)SuccessCount / TotalCount) * 100.0 : 0.0;
    public bool IsWinner { get; set; }

    public string SuccessText => $"{Math.Round(SuccessRate)}% доступности";
    public string PingText => AvgPingMs > 0 ? $"{AvgPingMs} мс" : "таймаут";
}

public static class DiagnosticResultManager
{
    public static bool HasUnseenResults { get; set; } = false;
    public static DateTime LastRunTime { get; set; } = DateTime.MinValue;
    public static List<DiagnosticPresetScore> BestPresets { get; private set; } = new();
    public static List<DiagnosticPresetScore> WorstPresets { get; private set; } = new();

    public static void SaveResults(List<DiagnosticPresetScore> scores)
    {
        if (scores == null || scores.Count == 0) return;

        var ordered = scores
            .OrderByDescending(s => s.SuccessRate)
            .ThenBy(s => s.AvgPingMs > 0 ? s.AvgPingMs : long.MaxValue)
            .ToList();

        if (ordered.Count > 0)
        {
            ordered[0].IsWinner = true;
        }

        BestPresets = ordered.Where(s => s.SuccessRate > 0).Take(4).ToList();
        WorstPresets = ordered.Where(s => s.SuccessRate == 0 || s.AvgPingMs < 0).Take(4).ToList();

        HasUnseenResults = true;
        LastRunTime = DateTime.Now;
    }

    public static void MarkAsSeen()
    {
        HasUnseenResults = false;
    }
}
