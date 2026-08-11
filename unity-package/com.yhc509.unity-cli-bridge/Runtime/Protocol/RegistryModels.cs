#nullable enable
using System;

namespace UnityCli.Protocol
{
    [Serializable]
    public sealed class InstanceRegistry
    {
        public string activeProjectRoot = string.Empty;
        public bool activeProjectRootPinned;
        public string? activeProjectHash;
        public InstanceRecord[] instances = Array.Empty<InstanceRecord>();
    }

    [Serializable]
    public sealed class InstanceRecord
    {
        public string projectRoot = string.Empty;
        public string projectName = string.Empty;
        public string projectHash = string.Empty;
        public string pipeName = string.Empty;
        // Auth tokens live in per-instance owner-only sidecars, not in the shared registry
        // (see InstanceRegistryFile.WriteTokenSidecar/ReadTokenSidecar). Keeping this field
        // in memory lets CLI send paths continue to consume target.token without persisting it.
        [NonSerialized]
#if !UNITY_5_3_OR_NEWER
        [System.Text.Json.Serialization.JsonIgnore]
#endif
        public string token = string.Empty;
        public int editorProcessId;
        public string unityVersion = string.Empty;
        public string state = "offline";
        public string lastSeenUtc = string.Empty;
        // "gui" | "headless" | "headless-nographics"; empty string = written by an older bridge (unknown).
        public string editorMode = string.Empty;
        public string[] capabilities = Array.Empty<string>();
    }
}
