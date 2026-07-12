#nullable enable
using System.Globalization;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    internal static class UnityObjectIdentity
    {
        public static ulong GetId(UnityEngine.Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return EntityId.ToULong(obj.GetEntityId());
#else
            return unchecked((ulong)(long)obj.GetInstanceID());
#endif
        }

        public static long GetWireId(UnityEngine.Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            return unchecked((long)EntityId.ToULong(obj.GetEntityId()));
#else
            return (long)obj.GetInstanceID();
#endif
        }

        public static UnityEngine.Object? FindById(ulong id)
        {
            if (id == 0UL)
            {
                return null;
            }

#if UNITY_6000_4_OR_NEWER
            return EditorUtility.EntityIdToObject(EntityId.FromULong(id));
#else
            int instanceId = unchecked((int)(long)id);
            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        public static UnityEngine.Object? FindSessionObject(string key)
        {
#if UNITY_6000_4_OR_NEWER
            string storedId = SessionState.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(storedId)
                || !ulong.TryParse(storedId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id))
            {
                return null;
            }

            return FindById(id);
#else
            int instanceId = SessionState.GetInt(key, 0);
            if (instanceId == 0)
            {
                return null;
            }

            return EditorUtility.InstanceIDToObject(instanceId);
#endif
        }

        public static void StoreSessionObject(string key, UnityEngine.Object obj)
        {
#if UNITY_6000_4_OR_NEWER
            string id = GetId(obj).ToString(CultureInfo.InvariantCulture);
            SessionState.SetString(key, id);
#else
            SessionState.SetInt(key, obj.GetInstanceID());
#endif
        }

        public static void EraseSessionObject(string key)
        {
#if UNITY_6000_4_OR_NEWER
            SessionState.EraseString(key);
#else
            SessionState.EraseInt(key);
#endif
        }
    }
}
