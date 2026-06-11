#nullable enable
using UnityEngine;
using UnityEngine.Events;

namespace UnityCliBridge.Bridge
{
    /// <summary>
    /// Inspector-friendly marker that exposes a non-UI world object as a QA tap target.
    /// Attach to any GameObject; set a label, an optional anchor, and optionally wire onQaTap.
    /// </summary>
    [AddComponentMenu("Unity CLI Bridge/QA Tappable")]
    public sealed class QaTappable : MonoBehaviour, IQaTappable
    {
        [Tooltip("Label shown in qa world-dump. Defaults to the GameObject name when empty.")]
        public string label = string.Empty;

        [Tooltip("World-space anchor for the tap point. Defaults to this transform when unset.")]
        public Transform? anchor;

        [Tooltip("Invoked when tapped via qa tap --target. With persistent listeners the bridge invokes it directly; otherwise it simulates a coordinate tap.")]
        public UnityEvent onQaTap = new UnityEvent();

        public string QaLabel => string.IsNullOrEmpty(label) ? name : label;

        public Transform? QaAnchor => anchor != null ? anchor : transform;

        public bool TryQaTap()
        {
            if (onQaTap == null || onQaTap.GetPersistentEventCount() == 0)
            {
                return false;
            }

            onQaTap.Invoke();
            return true;
        }
    }
}
