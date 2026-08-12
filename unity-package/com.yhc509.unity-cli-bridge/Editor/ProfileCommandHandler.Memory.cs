#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.Profiling;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal sealed partial class ProfileCommandHandler
    {
        // Counter names missing on the current Unity version resolve to recorder.Valid == false and are
        // reported through `unavailable` instead of failing the command. Prune dead names once measured live.
        private static readonly ProfileCounterSpec[] MemoryReportCounters =
        {
            new ProfileCounterSpec(ProfilerCategory.Memory, "Total Used Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Total Reserved Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "System Used Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "GC Used Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "GC Reserved Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Gfx Used Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Gfx Reserved Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Audio Used Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Video Used Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Texture Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Texture Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Mesh Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Mesh Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Material Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Material Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "AnimationClip Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "AnimationClip Memory", "bytes"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Asset Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "GameObject Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Scene Object Count", "count"),
            new ProfileCounterSpec(ProfilerCategory.Memory, "Object Count", "count"),
        };

        private void StartMemoryDeferred(
            ProfileMemoryArgs args,
            TaskCompletionSource<ResponseEnvelope> completion,
            string projectHash,
            string requestId)
        {
            int frames = args.frames > 0
                ? Math.Min(args.frames, ProtocolConstants.MaxProfileStatsFrames)
                : ProtocolConstants.DefaultProfileMemoryFrames;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var recorders = new List<(ProfileCounterSpec Spec, ProfilerRecorder Recorder)>();
            var unavailable = new List<string>();
            foreach (ProfileCounterSpec spec in MemoryReportCounters)
            {
                ProfilerRecorder recorder = ProfilerRecorder.StartNew(spec.Category, spec.Name, frames);
                if (!recorder.Valid)
                {
                    unavailable.Add(spec.Name);
                    recorder.Dispose();
                    continue;
                }

                recorders.Add((spec, recorder));
            }

            int ticks = 0;

            void DisposeAll()
            {
                foreach ((_, ProfilerRecorder recorder) in recorders)
                {
                    recorder.Dispose();
                }
            }

            void Poll()
            {
                if (completion.Task.IsCompleted)
                {
                    EditorApplication.update -= Poll;
                    DisposeAll();
                    return;
                }

                try
                {
                    ticks++;
                    if (stopwatch.Elapsed.TotalSeconds >= ProtocolConstants.ProfileStatsTimeoutSeconds)
                    {
                        EditorApplication.update -= Poll;
                        DisposeAll();
                        completion.TrySetResult(ResponseEnvelope.Failure(
                            requestId,
                            projectHash,
                            ProtocolConstants.ErrorProfileTimeout,
                            $"profile memory가 {ProtocolConstants.ProfileStatsTimeoutSeconds}초 안에 {frames}프레임을 수집하지 못했습니다.",
                            true,
                            stopwatch.ElapsedMilliseconds,
                            ProtocolConstants.TransportLive));
                        return;
                    }

                    if (ticks < frames)
                    {
                        return;
                    }

                    EditorApplication.update -= Poll;
                    var counters = new List<ProfileCounterStat>();
                    var samples = new List<ProfilerRecorderSample>(frames);
                    foreach ((ProfileCounterSpec spec, ProfilerRecorder recorder) in recorders)
                    {
                        samples.Clear();
                        recorder.CopyTo(samples);
                        var values = new List<double>(samples.Count);
                        foreach (ProfilerRecorderSample sample in samples)
                        {
                            values.Add(sample.Value);
                        }

                        if (values.Count == 0)
                        {
                            unavailable.Add(spec.Name);
                            continue;
                        }

                        double[] array = values.ToArray();
                        double[] sorted = (double[])array.Clone();
                        Array.Sort(sorted);
                        counters.Add(new ProfileCounterStat
                        {
                            name = spec.Name,
                            category = spec.Category.Name,
                            unit = spec.Unit,
                            min = sorted[0],
                            median = ProfileStatMath.Median(array),
                            p95 = ProfileStatMath.Percentile(sorted, 95),
                            max = sorted[sorted.Length - 1],
                        });
                    }

                    DisposeAll();
                    var payload = new ProfileMemoryPayload
                    {
                        reportId = Guid.NewGuid().ToString("N"),
                        mode = EditorApplication.isPlaying ? "playmode" : "editmode",
                        frames = frames,
                        unityVersion = Application.unityVersion,
                        capturedAtUtc = DateTime.UtcNow.ToString("O"),
                        counters = counters.ToArray(),
                        unavailable = unavailable.ToArray(),
                    };
                    WriteMemorySidecar(payload);
                    completion.TrySetResult(CreateSuccessResponse(requestId, projectHash, payload, stopwatch.ElapsedMilliseconds));
                }
                catch (Exception exception)
                {
                    EditorApplication.update -= Poll;
                    DisposeAll();
                    completion.TrySetResult(CreateFailureResponse(requestId, projectHash, exception, stopwatch.ElapsedMilliseconds));
                }
            }

            EditorApplication.update += Poll;
        }

        private static void WriteMemorySidecar(ProfileMemoryPayload report)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string path = Path.Combine(
                projectRoot,
                ProtocolConstants.MemoryReportsDirectoryRelative.Replace('/', Path.DirectorySeparatorChar),
                report.reportId + ".json");
            var sidecar = new ProfileMemorySidecarFile
            {
                schemaVersion = 1,
                reportId = report.reportId,
                report = report,
            };
            AtomicFileUtility.WriteAllText(path, ProtocolJson.Serialize(sidecar));
            AtomicFileUtility.CleanupTempFiles(Path.GetDirectoryName(path)!);
        }
    }
}
