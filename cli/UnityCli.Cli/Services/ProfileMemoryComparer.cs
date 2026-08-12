#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

/// <summary>Local diff of two memory report sidecars. No IPC.</summary>
internal static class ProfileMemoryComparer
{
    private const string TotalCounterName = "Total Used Memory";
    private const string GcCounterName = "GC Used Memory";

    internal static ResponseEnvelope Run(ParsedCommand parsed, string? projectRoot)
    {
        double threshold = parsed.ProfileThresholdPercent ?? ProtocolConstants.DefaultProfileMemoryThresholdPercent;
        if (!double.IsFinite(threshold) || threshold < 0)
        {
            return ResponseEnvelope.Failure(
                "local", null, "CLI_USAGE", "--threshold는 0 이상의 유한한 값이어야 합니다.", false, 0, "cli");
        }

        if (!ProfileSidecarLoader.TryLoadMemory(
                projectRoot, parsed.ProfileCompareBaseId, out ProfileMemorySidecarFile baseSidecar, out ResponseEnvelope? baseFailure))
        {
            return baseFailure!;
        }

        if (!ProfileSidecarLoader.TryLoadMemory(
                projectRoot, parsed.ProfileCompareHeadId, out ProfileMemorySidecarFile headSidecar, out ResponseEnvelope? headFailure))
        {
            return headFailure!;
        }

        // Check base first, then head, so the reported side is deterministic when both are unusable.
        if (!TryEnsureUsable(baseSidecar, "base", out ResponseEnvelope? baseUnusable))
        {
            return baseUnusable!;
        }

        if (!TryEnsureUsable(headSidecar, "head", out ResponseEnvelope? headUnusable))
        {
            return headUnusable!;
        }

        int limit = parsed.ProfileLimit ?? ProtocolConstants.DefaultProfileMemoryCompareLimit;
        ProfileMemoryComparePayload payload = Compare(baseSidecar, headSidecar, threshold, limit);

        return ResponseEnvelope.Success(
            "local",
            null,
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            0,
            "cli");
    }

    private static bool TryEnsureUsable(ProfileMemorySidecarFile sidecar, string side, out ResponseEnvelope? failure)
    {
        if (sidecar.report.counters.Length > 0)
        {
            failure = null;
            return true;
        }

        failure = ResponseEnvelope.Failure(
            "local",
            null,
            ProtocolConstants.ErrorProfileFailed,
            $"{side} 리포트 `{sidecar.reportId}`에는 카운터가 없습니다. 다시 측정하세요.",
            false,
            0,
            "cli");
        return false;
    }

    internal static ProfileMemoryComparePayload Compare(
        ProfileMemorySidecarFile baseSidecar,
        ProfileMemorySidecarFile headSidecar,
        double thresholdPercent,
        int limit)
    {
        ProfileMemoryPayload baseReport = baseSidecar.report;
        ProfileMemoryPayload headReport = headSidecar.report;

        var notes = new List<string>();
        if (!string.Equals(baseReport.mode, headReport.mode, StringComparison.Ordinal))
        {
            notes.Add($"mode가 다릅니다 (base={baseReport.mode}, head={headReport.mode}) — 값 차이가 모드 차이일 수 있습니다.");
        }

        if (!string.Equals(baseReport.unityVersion, headReport.unityVersion, StringComparison.Ordinal))
        {
            notes.Add($"unityVersion이 다릅니다 (base={baseReport.unityVersion}, head={headReport.unityVersion}).");
        }

        if (baseReport.frames != headReport.frames)
        {
            notes.Add($"샘플링 프레임 수가 다릅니다 (base={baseReport.frames}, head={headReport.frames}).");
        }

        var payload = new ProfileMemoryComparePayload
        {
            baseReport = BuildSide(baseSidecar),
            headReport = BuildSide(headSidecar),
            thresholdPercent = thresholdPercent,
            totalUsedBytes = BuildDelta(FindMedian(baseReport, TotalCounterName), FindMedian(headReport, TotalCounterName)),
            gcUsedBytes = BuildDelta(FindMedian(baseReport, GcCounterName), FindMedian(headReport, GcCounterName)),
        };

        bool totalMissing = FindMedian(baseReport, TotalCounterName) is null
            || FindMedian(headReport, TotalCounterName) is null;
        if (totalMissing)
        {
            notes.Insert(0, $"`{TotalCounterName}` 카운터가 한쪽에 없어 verdict를 판정할 수 없습니다 — unchanged로 고정합니다.");
            payload.verdict = "unchanged";
        }
        else if (!payload.totalUsedBytes.deltaPercentAvailable)
        {
            notes.Insert(0, "base Total Used Memory가 0 이하라 퍼센트가 정의되지 않습니다 — verdict는 unchanged로 고정합니다.");
            payload.verdict = "unchanged";
        }
        else if (payload.totalUsedBytes.deltaPercent > thresholdPercent)
        {
            payload.verdict = "regression";
        }
        else if (payload.totalUsedBytes.deltaPercent < -thresholdPercent)
        {
            payload.verdict = "improvement";
        }
        else
        {
            payload.verdict = "unchanged";
        }

        // Per-counter deltas cover only counters present on both sides. The base array's
        // declaration order is the tie-break ordinal, so --limit output is reproducible.
        var increases = new List<(ProfileMemoryCounterDelta Delta, int Ordinal)>();
        var decreases = new List<(ProfileMemoryCounterDelta Delta, int Ordinal)>();
        for (int i = 0; i < baseReport.counters.Length; i++)
        {
            ProfileCounterStat baseStat = baseReport.counters[i];
            ProfileCounterStat? headStat = headReport.counters
                .FirstOrDefault(c => string.Equals(c.name, baseStat.name, StringComparison.Ordinal));
            if (headStat is null)
            {
                notes.Add($"base에만 있는 카운터: {baseStat.name}");
                continue;
            }

            double delta = headStat.median - baseStat.median;
            if (delta == 0)
            {
                continue;
            }

            var entry = new ProfileMemoryCounterDelta
            {
                name = baseStat.name,
                unit = baseStat.unit,
                baseMedian = baseStat.median,
                headMedian = headStat.median,
                delta = delta,
                deltaPercent = baseStat.median > 0 ? delta / baseStat.median * 100.0 : 0,
                deltaPercentAvailable = baseStat.median > 0,
            };
            if (delta > 0)
            {
                increases.Add((entry, i));
            }
            else
            {
                decreases.Add((entry, i));
            }
        }

        foreach (ProfileCounterStat headStat in headReport.counters)
        {
            if (!baseReport.counters.Any(c => string.Equals(c.name, headStat.name, StringComparison.Ordinal)))
            {
                notes.Add($"head에만 있는 카운터: {headStat.name}");
            }
        }

        payload.truncated = increases.Count > limit || decreases.Count > limit;
        payload.increases = TakeSorted(increases, limit);
        payload.decreases = TakeSorted(decreases, limit);
        payload.notes = notes.ToArray();
        return payload;
    }

    private static ProfileMemoryCounterDelta[] TakeSorted(
        List<(ProfileMemoryCounterDelta Delta, int Ordinal)> entries, int limit)
    {
        return entries
            .OrderByDescending(entry => Math.Abs(entry.Delta.delta))
            .ThenBy(entry => entry.Ordinal)
            .Take(Math.Max(0, limit))
            .Select(entry => entry.Delta)
            .ToArray();
    }

    private static ProfileMemoryCompareSide BuildSide(ProfileMemorySidecarFile sidecar)
    {
        return new ProfileMemoryCompareSide
        {
            reportId = sidecar.reportId,
            frames = sidecar.report.frames,
            mode = sidecar.report.mode,
            unityVersion = sidecar.report.unityVersion,
            capturedAtUtc = sidecar.report.capturedAtUtc,
        };
    }

    private static double? FindMedian(ProfileMemoryPayload report, string counterName)
    {
        foreach (ProfileCounterStat stat in report.counters)
        {
            if (string.Equals(stat.name, counterName, StringComparison.Ordinal))
            {
                return stat.median;
            }
        }

        return null;
    }

    private static ProfileCompareDelta BuildDelta(double? baseValue, double? headValue)
    {
        double baseNumber = baseValue ?? 0;
        double headNumber = headValue ?? 0;
        var delta = new ProfileCompareDelta
        {
            baseValue = baseNumber,
            headValue = headNumber,
            delta = headNumber - baseNumber,
        };
        if (baseValue is null || headValue is null || baseNumber <= 0)
        {
            delta.deltaPercentAvailable = false;
            delta.deltaPercent = 0;
        }
        else
        {
            delta.deltaPercent = (headNumber - baseNumber) / baseNumber * 100.0;
        }

        return delta;
    }
}
