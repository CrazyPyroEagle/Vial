using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Vial
{
    internal static class UnityUtils
    {
        public static IEnumerable<T> GetMany<T>(this UnityEngine.SceneManagement.Scene scene) => scene.GetRootGameObjects().SelectMany(ro => ro.GetComponentsInChildren<T>());

        public static T GetUnique<T>(this UnityEngine.SceneManagement.Scene scene)
        {
            T result = scene.GetMany<T>().FirstOrDefault();
            if (result == default) Debug.LogErrorFormat("[Vial] Failed to find {0} in {1} scene", nameof(T), scene.name);
            return result;
        }
    }
}
