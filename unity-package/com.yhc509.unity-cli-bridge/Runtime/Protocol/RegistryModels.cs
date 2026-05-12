#nullable enable
using System;

namespace UnityCli.Protocol
{
    [Serializable]
    public sealed class InstanceRegistry
    {
        public string activeProjectRoot = string.Empty;
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
        public int editorProcessId;
        public string unityVersion = string.Empty;
        public string state = "offline";
        public string lastSeenUtc = string.Empty;
        public string[] capabilities = Array.Empty<string>();
    }
}
