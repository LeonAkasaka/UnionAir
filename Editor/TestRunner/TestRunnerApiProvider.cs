using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace LeonAkasaka.UnionAir.Editor
{
    internal static class TestRunnerApiProvider
    {
        private static TestRunnerApi _instance;

        internal static TestRunnerApi Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = ScriptableObject.CreateInstance<TestRunnerApi>();
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                }
                return _instance;
            }
        }

        internal static void Dispose()
        {
            if (_instance != null)
                Object.DestroyImmediate(_instance);
            _instance = null;
        }
    }
}
