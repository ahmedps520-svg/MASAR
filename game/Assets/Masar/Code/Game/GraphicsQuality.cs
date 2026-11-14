using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Masar.Game
{
    /// <summary>
    /// Applies the graphics tier. Each Unity quality level has its own URP asset
    /// (see Editor/MasarProjectSetup), so a tier changes real pipeline parameters:
    /// render scale, shadow distance and resolution, cascades, SSAO, HDR and MSAA.
    /// </summary>
    public static class GraphicsQuality
    {
        public static string[] Names => QualitySettings.names;

        public static int DefaultLevel()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return Mathf.Min(2, QualitySettings.names.Length - 1);   // "High" on iPad until measured
#else
            return QualitySettings.names.Length - 1;                   // "Ultra" on desktop
#endif
        }

        public static void Apply(MasarSettings s)
        {
            int level = s.qualityLevel < 0 ? DefaultLevel() : Mathf.Clamp(s.qualityLevel, 0, QualitySettings.names.Length - 1);
            if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = s.targetFrameRate;
        }

        public static float CurrentRenderScale()
        {
            return GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.renderScale : 1f;
        }
    }
}
