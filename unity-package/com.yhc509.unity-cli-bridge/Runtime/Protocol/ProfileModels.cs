#nullable enable
using System;

namespace UnityCli.Protocol
{
    [Serializable]
    public sealed class ProfileStatsArgs
    {
        public int frames;
        public string preset = string.Empty;
    }

    [Serializable]
    public sealed class ProfileCounterStat
    {
        public string name = string.Empty;
        public string category = string.Empty;
        public string unit = string.Empty;
        public double min;
        public double median;
        public double p95;
        public double max;
    }

    [Serializable]
    public sealed class ProfileStatsPayload
    {
        public int frames;
        public string preset = string.Empty;
        public string mode = string.Empty;
        public ProfileCounterStat[] counters = Array.Empty<ProfileCounterStat>();
        public string[] unavailable = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ProfileCaptureStartArgs
    {
        public int frames;
        public int durationSeconds;
        public float budgetMs;
    }

    [Serializable]
    public sealed class ProfileStatusArgs
    {
        public string? captureId;
    }

    [Serializable]
    public sealed class ProfileCaptureStartedPayload
    {
        public string captureId = string.Empty;
        public string status = string.Empty;
        public string startedAt = string.Empty;
        public int requestedFrames;
        public int durationSeconds;
        public float budgetMs;
    }

    [Serializable]
    public sealed class ProfileFrameTimeStats
    {
        public double medianMs;
        public double p95Ms;
        public double worstMs;
        public int worstFrame = -1;
        public int overBudgetCount;
    }

    [Serializable]
    public sealed class ProfileVerdict
    {
        public string bound = "unknown";
        public string basis = "none";
        public double gpuMedianMs = -1;
    }

    [Serializable]
    public sealed class ProfileSpike
    {
        public int frame;
        public double ms;
        public string topMarker = string.Empty;
        public double topMarkerSelfMs;
    }

    [Serializable]
    public sealed class ProfileHotspot
    {
        public string marker = string.Empty;
        public double selfMedianMs;
        public double selfP95Ms;
        public double selfTotalMs;
        public long calls;
    }

    [Serializable]
    public sealed class ProfileGcEntry
    {
        public string marker = string.Empty;
        public long bytesTotal;
        public int framesWithAlloc;
    }

    [Serializable]
    public sealed class ProfileSummaryPayload
    {
        public string captureId = string.Empty;
        public string status = string.Empty;
        public int capturedFrames;
        public int requestedFrames;
        public bool truncated;
        public string mode = string.Empty;
        public string unityVersion = string.Empty;
        public float budgetMs;
        public ProfileFrameTimeStats frameTime = new ProfileFrameTimeStats();
        public ProfileVerdict verdict = new ProfileVerdict();
        public ProfileSpike[] spikes = Array.Empty<ProfileSpike>();
        public ProfileHotspot[] hotspots = Array.Empty<ProfileHotspot>();
        public ProfileGcEntry[] gcTop = Array.Empty<ProfileGcEntry>();
        public string sidecarPath = string.Empty;
    }

    [Serializable]
    public sealed class ProfileFrameMarker
    {
        public string m = string.Empty;
        public double self;
        public long gc;
        public int calls;
    }

    [Serializable]
    public sealed class ProfileFrameEntry
    {
        public int i;
        public double ms;
        public double gpuMs = -1;
        public ProfileFrameMarker[] top = Array.Empty<ProfileFrameMarker>();
    }

    [Serializable]
    public sealed class ProfileMarkerEntry
    {
        public string m = string.Empty;
        public double selfTotalMs;
        public double selfMedianMs;
        public double selfP95Ms;
        public long gcBytes;
        public long calls;
        public int frames;
    }

    [Serializable]
    public sealed class ProfileSidecarFile
    {
        public int schemaVersion = 1;
        public string captureId = string.Empty;
        public string createdUtc = string.Empty;
        public ProfileSummaryPayload summary = new ProfileSummaryPayload();
        public ProfileFrameEntry[] frames = Array.Empty<ProfileFrameEntry>();
        public ProfileMarkerEntry[] markers = Array.Empty<ProfileMarkerEntry>();
    }

    [Serializable]
    public sealed class ProfileFrameAppearance
    {
        public int frame;
        public double selfMs;
        public long gcBytes;
        public int calls;
    }

    [Serializable]
    public sealed class ProfileAnalyzeMarkerPayload
    {
        public string captureId = string.Empty;
        public ProfileMarkerEntry marker = new ProfileMarkerEntry();
        public ProfileFrameAppearance[] appearances = Array.Empty<ProfileFrameAppearance>();
        public bool appearancesFromTopOnly = true;
    }

    [Serializable]
    public sealed class ProfileAnalyzeFramePayload
    {
        public string captureId = string.Empty;
        public float budgetMs;
        public bool overBudget;
        public ProfileFrameEntry frame = new ProfileFrameEntry();
    }

    [Serializable]
    public sealed class ProfileAnalyzeGcPayload
    {
        public string captureId = string.Empty;
        public ProfileGcEntry[] entries = Array.Empty<ProfileGcEntry>();
        public bool truncated;
    }

    [Serializable]
    public sealed class ProfileAnalyzeSpikesPayload
    {
        public string captureId = string.Empty;
        public float budgetMs;
        public ProfileFrameEntry[] frames = Array.Empty<ProfileFrameEntry>();
    }
}
