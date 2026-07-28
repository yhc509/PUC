#nullable enable
using System;
using System.Linq;
using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

/// <summary>Local drill-down over a finished capture's sidecar. No IPC.</summary>
internal static class ProfileAnalyzer
{
    internal static ResponseEnvelope Run(ParsedCommand parsed, string? projectRoot)
    {
        if (!ProfileSidecarLoader.TryLoad(
                projectRoot, parsed.ProfileCaptureId, out ProfileSidecarFile sidecar, out ResponseEnvelope? loadFailure))
        {
            return loadFailure!;
        }

        int limit = parsed.ProfileLimit ?? ProtocolConstants.DefaultProfileListLimit;
        object payload;
        if (!string.IsNullOrEmpty(parsed.ProfileAnalyzeMarker))
        {
            payload = BuildMarkerPayload(sidecar, parsed.ProfileAnalyzeMarker!, limit, out ResponseEnvelope? failure);
            if (failure != null)
            {
                return failure;
            }
        }
        else if (parsed.ProfileAnalyzeFrame.HasValue)
        {
            ProfileFrameEntry? frame = sidecar.frames.FirstOrDefault(f => f.i == parsed.ProfileAnalyzeFrame.Value);
            if (frame is null)
            {
                return ResponseEnvelope.Failure(
                    "local",
                    null,
                    ProtocolConstants.ErrorProfileNotFound,
                    $"프레임 {parsed.ProfileAnalyzeFrame.Value}이 캡처 범위에 없습니다 (0..{(sidecar.frames.Length == 0 ? 0 : sidecar.frames[^1].i)}).",
                    false,
                    0,
                    "cli");
            }

            payload = new ProfileAnalyzeFramePayload
            {
                captureId = sidecar.captureId,
                budgetMs = sidecar.summary.budgetMs,
                overBudget = frame.ms > sidecar.summary.budgetMs,
                frame = frame,
            };
        }
        else if (parsed.ProfileAnalyzeGc)
        {
            ProfileGcEntry[] all = sidecar.markers
                .Where(m => m.gcBytes > 0)
                .OrderByDescending(m => m.gcBytes)
                .Select(m => new ProfileGcEntry { marker = m.m, bytesTotal = m.gcBytes, framesWithAlloc = m.frames })
                .ToArray();
            payload = new ProfileAnalyzeGcPayload
            {
                captureId = sidecar.captureId,
                entries = all.Take(limit).ToArray(),
                truncated = all.Length > limit,
            };
        }
        else
        {
            int[] spikeFrames = sidecar.summary.spikes.Select(s => s.frame).ToArray();
            payload = new ProfileAnalyzeSpikesPayload
            {
                captureId = sidecar.captureId,
                budgetMs = sidecar.summary.budgetMs,
                frames = sidecar.frames.Where(f => spikeFrames.Contains(f.i)).Take(limit).ToArray(),
            };
        }

        return ResponseEnvelope.Success(
            "local",
            null,
            JsonSerializer.SerializeToElement(payload, ProtocolJson.Default),
            0,
            "cli");
    }

    private static object BuildMarkerPayload(
        ProfileSidecarFile sidecar,
        string markerName,
        int limit,
        out ResponseEnvelope? failure)
    {
        failure = null;
        ProfileMarkerEntry? marker = sidecar.markers.FirstOrDefault(
            m => string.Equals(m.m, markerName, StringComparison.Ordinal));
        if (marker is null)
        {
            failure = ResponseEnvelope.Failure(
                "local",
                null,
                ProtocolConstants.ErrorProfileNotFound,
                $"마커 `{markerName}`가 캡처에 없습니다. `profile analyze --gc`나 summary의 hotspots에서 정확한 이름을 확인하세요.",
                false,
                0,
                "cli");
            return new object();
        }

        ProfileFrameAppearance[] appearances = sidecar.frames
            .SelectMany(f => f.top
                .Where(t => string.Equals(t.m, markerName, StringComparison.Ordinal))
                .Select(t => new ProfileFrameAppearance { frame = f.i, selfMs = t.self, gcBytes = t.gc, calls = t.calls }))
            .OrderByDescending(a => a.selfMs)
            .Take(limit)
            .ToArray();

        return new ProfileAnalyzeMarkerPayload
        {
            captureId = sidecar.captureId,
            marker = marker,
            appearances = appearances,
            appearancesFromTopOnly = true,
        };
    }
}
