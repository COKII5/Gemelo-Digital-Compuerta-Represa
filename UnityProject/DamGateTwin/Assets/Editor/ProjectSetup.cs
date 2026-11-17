using UnityEditor;
using UnityEditor.Build;

namespace DamTwin.EditorTools
{
    /// <summary>
    /// One-time project configuration applied when the editor loads:
    ///  - API Compatibility Level = .NET Framework (required by System.IO.Ports).
    ///  - Run In Background = true (keep reading the serial port when the window loses focus).
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        static ProjectSetup()
        {
            EditorApplication.delayCall += Apply;
        }

        [MenuItem("Digital Twin/Apply Project Settings")]
        public static void Apply()
        {
            NamedBuildTarget target = NamedBuildTarget.Standalone;
            if (PlayerSettings.GetApiCompatibilityLevel(target) != ApiCompatibilityLevel.NET_Unity_4_8)
            {
                PlayerSettings.SetApiCompatibilityLevel(target, ApiCompatibilityLevel.NET_Unity_4_8);
                UnityEngine.Debug.Log("[DamTwin] API Compatibility Level set to .NET Framework (needed for System.IO.Ports).");
            }

            if (!PlayerSettings.runInBackground)
            {
                PlayerSettings.runInBackground = true;
            }

            const string productName = "Gemelo Digital - Compuerta de Represa";
            if (PlayerSettings.productName != productName)
            {
                PlayerSettings.productName = productName;
            }
        }
    }
}
