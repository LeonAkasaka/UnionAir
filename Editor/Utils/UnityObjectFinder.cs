using System;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class UnityObjectFinder
    {
        internal static T[] FindActive<T>() where T : UnityEngine.Object
        {
#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsByType<T>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#endif
        }

        internal static UnityEngine.Object[] FindActive(Type type)
        {
#if UNITY_6000_4_OR_NEWER
            return UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Exclude);
#else
            return UnityEngine.Object.FindObjectsByType(
                type,
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
#endif
        }
    }
}
