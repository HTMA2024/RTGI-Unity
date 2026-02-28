using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace URPSSGI
{

    [VolumeComponentEditor(typeof(SSGIVolumeComponent))]
    public sealed class SSGIVolumeComponentEditor : VolumeComponentEditor
    {

        private bool m_GeneralFoldout;
        private bool m_RayMarchFoldout;
        private bool m_RayTraceFoldout;
        private bool m_DenoiseFoldout;
        private bool m_CompositeFoldout;
        private bool m_DebugFoldout;

        private const string k_GeneralFoldoutKey   = "SSGI_Foldout_General";
        private const string k_RayMarchFoldoutKey  = "SSGI_Foldout_RayMarch";
        private const string k_RayTraceFoldoutKey  = "SSGI_Foldout_RayTrace";
        private const string k_DenoiseFoldoutKey   = "SSGI_Foldout_Denoise";
        private const string k_CompositeFoldoutKey = "SSGI_Foldout_Composite";
        private const string k_DebugFoldoutKey     = "SSGI_Foldout_Debug";

        private SerializedDataParameter m_Enable;
        private SerializedDataParameter m_FullResolution;
        private SerializedDataParameter m_UseAccurateNormals;
        private SerializedDataParameter m_MultiBounce;
        private SerializedDataParameter m_DeferredLighting;
        private SerializedDataParameter m_GIMode;

        private SerializedDataParameter m_MaxRaySteps;
        private SerializedDataParameter m_DepthBufferThickness;
        private SerializedDataParameter m_RayMissFallback;

        private SerializedDataParameter m_RtRayLength;
        private SerializedDataParameter m_RtSampleCount;
        private SerializedDataParameter m_RtBounceCount;
        private SerializedDataParameter m_RtClampValue;
        private SerializedDataParameter m_RtTextureLodBias;
        private SerializedDataParameter m_RtLastBounceFallbackHierarchy;
        private SerializedDataParameter m_RtAmbientProbeDimmer;
        private SerializedDataParameter m_RtDenoise;
        private SerializedDataParameter m_RtDenoiserRadius;
        private SerializedDataParameter m_RtHalfResolutionDenoiser;
        private SerializedDataParameter m_RtSecondDenoiserPass;
        private SerializedDataParameter m_RtMixedRaySteps;
        private SerializedDataParameter m_RtShadowRay;

        private SerializedDataParameter m_RtRayBias;
        private SerializedDataParameter m_RtDistantRayBias;

        private SerializedDataParameter m_EnableRTAO;
        private SerializedDataParameter m_RTAORadius;
        private SerializedDataParameter m_RTAOIntensity;

        private SerializedDataParameter m_Denoise;
        private SerializedDataParameter m_DenoiserRadius;
        private SerializedDataParameter m_HalfResolutionDenoiser;
        private SerializedDataParameter m_SecondDenoiserPass;

        private SerializedDataParameter m_CompositeIntensity;
        private SerializedDataParameter m_CompositeMode;
        private SerializedDataParameter m_DebugMode;
        private SerializedDataParameter m_DebugMipLevel;

        private static bool s_RayTracingSupportChecked;
        private static bool s_RayTracingSupported;

        public override void OnEnable()
        {
            PropertyFetcher<SSGIVolumeComponent> o = new PropertyFetcher<SSGIVolumeComponent>(serializedObject);

            m_GeneralFoldout   = EditorPrefs.GetBool(k_GeneralFoldoutKey, true);
            m_RayMarchFoldout  = EditorPrefs.GetBool(k_RayMarchFoldoutKey, true);
            m_RayTraceFoldout  = EditorPrefs.GetBool(k_RayTraceFoldoutKey, true);
            m_DenoiseFoldout   = EditorPrefs.GetBool(k_DenoiseFoldoutKey, true);
            m_CompositeFoldout = EditorPrefs.GetBool(k_CompositeFoldoutKey, true);
            m_DebugFoldout     = EditorPrefs.GetBool(k_DebugFoldoutKey, true);

            m_Enable              = Unpack(o.Find(x => x.enable));
            m_FullResolution      = Unpack(o.Find(x => x.fullResolution));
            m_UseAccurateNormals  = Unpack(o.Find(x => x.useAccurateNormals));
            m_MultiBounce         = Unpack(o.Find(x => x.multiBounce));
            m_DeferredLighting    = Unpack(o.Find(x => x.deferredLighting));
            m_GIMode              = Unpack(o.Find(x => x.giMode));

            m_MaxRaySteps         = Unpack(o.Find(x => x.maxRaySteps));
            m_DepthBufferThickness = Unpack(o.Find(x => x.depthBufferThickness));
            m_RayMissFallback     = Unpack(o.Find(x => x.rayMissFallback));

            m_RtRayLength                    = Unpack(o.Find(x => x.rtRayLength));
            m_RtSampleCount                  = Unpack(o.Find(x => x.rtSampleCount));
            m_RtBounceCount                  = Unpack(o.Find(x => x.rtBounceCount));
            m_RtClampValue                   = Unpack(o.Find(x => x.rtClampValue));
            m_RtTextureLodBias               = Unpack(o.Find(x => x.rtTextureLodBias));
            m_RtLastBounceFallbackHierarchy  = Unpack(o.Find(x => x.rtLastBounceFallbackHierarchy));
            m_RtAmbientProbeDimmer           = Unpack(o.Find(x => x.rtAmbientProbeDimmer));
            m_RtDenoise                      = Unpack(o.Find(x => x.rtDenoise));
            m_RtDenoiserRadius               = Unpack(o.Find(x => x.rtDenoiserRadius));
            m_RtHalfResolutionDenoiser       = Unpack(o.Find(x => x.rtHalfResolutionDenoiser));
            m_RtSecondDenoiserPass           = Unpack(o.Find(x => x.rtSecondDenoiserPass));
            m_RtMixedRaySteps                = Unpack(o.Find(x => x.rtMixedRaySteps));
            m_RtShadowRay                    = Unpack(o.Find(x => x.rtShadowRay));
            m_RtRayBias                      = Unpack(o.Find(x => x.rtRayBias));
            m_RtDistantRayBias               = Unpack(o.Find(x => x.rtDistantRayBias));

            m_EnableRTAO                     = Unpack(o.Find(x => x.enableRTAO));
            m_RTAORadius                     = Unpack(o.Find(x => x.rtaoRadius));
            m_RTAOIntensity                  = Unpack(o.Find(x => x.rtaoIntensity));

            m_Denoise             = Unpack(o.Find(x => x.denoise));
            m_DenoiserRadius      = Unpack(o.Find(x => x.denoiserRadius));
            m_HalfResolutionDenoiser = Unpack(o.Find(x => x.halfResolutionDenoiser));
            m_SecondDenoiserPass  = Unpack(o.Find(x => x.secondDenoiserPass));

            m_CompositeIntensity  = Unpack(o.Find(x => x.compositeIntensity));
            m_CompositeMode       = Unpack(o.Find(x => x.compositeMode));
            m_DebugMode           = Unpack(o.Find(x => x.debugMode));
            m_DebugMipLevel       = Unpack(o.Find(x => x.debugMipLevel));

            if (!s_RayTracingSupportChecked)
            {
                s_RayTracingSupported = RTASManager.IsRayTracingSupported();
                s_RayTracingSupportChecked = true;
            }
        }

        public override void OnInspectorGUI()
        {

            IndirectDiffuseMode giMode = (IndirectDiffuseMode)m_GIMode.value.intValue;
            bool showRayMarch = giMode == IndirectDiffuseMode.ScreenSpace
                             || giMode == IndirectDiffuseMode.Mixed
                             || giMode == IndirectDiffuseMode.MixedDDGI;
            bool showRayTrace = giMode == IndirectDiffuseMode.RayTraced
                             || giMode == IndirectDiffuseMode.Mixed
                             || giMode == IndirectDiffuseMode.MixedDDGI;

            m_GeneralFoldout = EditorGUILayout.Foldout(m_GeneralFoldout, "通用设置", true);
            EditorPrefs.SetBool(k_GeneralFoldoutKey, m_GeneralFoldout);
            if (m_GeneralFoldout)
            {
                PropertyField(m_Enable);
                PropertyField(m_GIMode);

                if (showRayTrace && !s_RayTracingSupported)
                {
                    EditorGUILayout.HelpBox(
                        "当前平台不支持硬件光线追踪，运行时将自动回退到 ScreenSpace 模式。",
                        MessageType.Warning);
                }

                if (giMode == IndirectDiffuseMode.MixedDDGI)
                {
                    if (!DDGI.DDGIResourceProvider.Current.isValid)
                    {
                        EditorGUILayout.HelpBox(
                            "MixedDDGI 模式需要场景中存在活跃的 DDGI 系统。\n" +
                            "请确保已添加 DDGIApplyGIRendererFeature 并配置 DDGIVolume。\n" +
                            "DDGI 不可用时将自动回退到标准 Mixed 模式。",
                            MessageType.Warning);
                    }
                }

                PropertyField(m_FullResolution);
                PropertyField(m_UseAccurateNormals);
                PropertyField(m_MultiBounce);

                if (giMode == IndirectDiffuseMode.ScreenSpace)
                {
                    PropertyField(m_DeferredLighting);
                }
                else if (giMode == IndirectDiffuseMode.Mixed || giMode == IndirectDiffuseMode.MixedDDGI)
                {

                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Toggle("Deferred Lighting", true);
                    EditorGUI.EndDisabledGroup();
                }
            }

            if (showRayMarch)
            {
                EditorGUILayout.Space();
                m_RayMarchFoldout = EditorGUILayout.Foldout(m_RayMarchFoldout, "光线步进", true);
                EditorPrefs.SetBool(k_RayMarchFoldoutKey, m_RayMarchFoldout);
                if (m_RayMarchFoldout)
                {
                    PropertyField(m_MaxRaySteps);
                    PropertyField(m_DepthBufferThickness);
                    PropertyField(m_RayMissFallback);

                    if (giMode == IndirectDiffuseMode.Mixed || giMode == IndirectDiffuseMode.MixedDDGI)
                    {
                        PropertyField(m_RtMixedRaySteps);
                    }
                }
            }

            if (showRayTrace)
            {
                EditorGUILayout.Space();
                m_RayTraceFoldout = EditorGUILayout.Foldout(m_RayTraceFoldout, "光线追踪", true);
                EditorPrefs.SetBool(k_RayTraceFoldoutKey, m_RayTraceFoldout);
                if (m_RayTraceFoldout)
                {
                    PropertyField(m_RtRayLength);
                    PropertyField(m_RtSampleCount);
                    PropertyField(m_RtBounceCount);
                    PropertyField(m_RtClampValue);
                    PropertyField(m_RtTextureLodBias);
                    PropertyField(m_RtLastBounceFallbackHierarchy);
                    PropertyField(m_RtAmbientProbeDimmer);
                    PropertyField(m_RtShadowRay);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("射线偏移（自相交防护）", EditorStyles.boldLabel);
                    PropertyField(m_RtRayBias);
                    PropertyField(m_RtDistantRayBias);

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("RTAO 环境光遮蔽", EditorStyles.boldLabel);
                    PropertyField(m_EnableRTAO);

                    if (m_EnableRTAO.value.boolValue)
                    {
                        PropertyField(m_RTAORadius);
                        PropertyField(m_RTAOIntensity);
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("RTGI 去噪", EditorStyles.boldLabel);
                    PropertyField(m_RtDenoise);

                    if (m_RtDenoise.value.boolValue)
                    {
                        PropertyField(m_RtDenoiserRadius);
                        PropertyField(m_RtHalfResolutionDenoiser);
                        PropertyField(m_RtSecondDenoiserPass);
                    }
                }
            }

            if (giMode == IndirectDiffuseMode.ScreenSpace)
            {
                EditorGUILayout.Space();
                m_DenoiseFoldout = EditorGUILayout.Foldout(m_DenoiseFoldout, "去噪", true);
                EditorPrefs.SetBool(k_DenoiseFoldoutKey, m_DenoiseFoldout);
                if (m_DenoiseFoldout)
                {
                    PropertyField(m_Denoise);

                    if (m_Denoise.value.boolValue)
                    {
                        PropertyField(m_DenoiserRadius);
                        PropertyField(m_HalfResolutionDenoiser);
                        PropertyField(m_SecondDenoiserPass);
                    }
                }
            }

            EditorGUILayout.Space();
            m_CompositeFoldout = EditorGUILayout.Foldout(m_CompositeFoldout, "GI 合成", true);
            EditorPrefs.SetBool(k_CompositeFoldoutKey, m_CompositeFoldout);
            if (m_CompositeFoldout)
            {
                PropertyField(m_CompositeIntensity);
                PropertyField(m_CompositeMode);
            }

            EditorGUILayout.Space();
            m_DebugFoldout = EditorGUILayout.Foldout(m_DebugFoldout, "调试", true);
            EditorPrefs.SetBool(k_DebugFoldoutKey, m_DebugFoldout);
            if (m_DebugFoldout)
            {
                PropertyField(m_DebugMode);

                SSGIDebugMode debugMode = (SSGIDebugMode)m_DebugMode.value.intValue;

                if (debugMode == SSGIDebugMode.DepthPyramid)
                {
                    PropertyField(m_DebugMipLevel);
                }

                bool denoise = m_Denoise.value.boolValue;

                if ((debugMode == SSGIDebugMode.AccumulationCount
                    || debugMode == SSGIDebugMode.DenoiseComparison
                    || debugMode == SSGIDebugMode.DenoisedGI)
                    && !denoise)
                {
                    EditorGUILayout.HelpBox("该调试模式需要启用去噪功能。", MessageType.Info);
                }

                if ((debugMode == SSGIDebugMode.RTGIOnly
                    || debugMode == SSGIDebugMode.RTGIRayLength
                    || debugMode == SSGIDebugMode.MixedMask
                    || debugMode == SSGIDebugMode.RTGINormal
                    || debugMode == SSGIDebugMode.RTAO
                    || debugMode == SSGIDebugMode.RTGIWithAO)
                    && giMode == IndirectDiffuseMode.ScreenSpace)
                {
                    EditorGUILayout.HelpBox(
                        "该调试模式仅在 RayTraced 或 Mixed 模式下有效，当前为 ScreenSpace 模式将输出黑色。",
                        MessageType.Info);
                }

                if ((debugMode == SSGIDebugMode.RTAO || debugMode == SSGIDebugMode.RTGIWithAO)
                    && !m_EnableRTAO.value.boolValue)
                {
                    EditorGUILayout.HelpBox(
                        "该调试模式需要在光线追踪折叠组中启用 RTAO。",
                        MessageType.Info);
                }

                if (debugMode == SSGIDebugMode.MixedMask
                    && giMode != IndirectDiffuseMode.Mixed
                    && giMode != IndirectDiffuseMode.MixedDDGI)
                {
                    EditorGUILayout.HelpBox(
                        "MixedMask 调试模式仅在 Mixed 或 MixedDDGI 模式下有效。",
                        MessageType.Info);
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("运行时统计", EditorStyles.boldLabel);

                SSGIVolumeComponent volume = target as SSGIVolumeComponent;
                if (volume != null && volume.IsActive())
                {
                    ref readonly SSGIRuntimeStats stats = ref SSGIRenderPass.LastStats;

                    EditorGUILayout.LabelField("工作分辨率",
                        string.Concat(stats.workingWidth.ToString(), " × ", stats.workingHeight.ToString()));
                    EditorGUILayout.LabelField("分辨率模式", stats.isFullResolution ? "全分辨率" : "半分辨率");
                    EditorGUILayout.LabelField("活跃 RT 数量", stats.activeRTCount.ToString());

                    string denoiseStatus = stats.denoiseEnabled
                        ? (stats.secondPassEnabled ? "启用（双遍）" : "启用（单遍）")
                        : "禁用";
                    EditorGUILayout.LabelField("去噪状态", denoiseStatus);
                    EditorGUILayout.LabelField("当前调试模式", stats.currentDebugMode.ToString());

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("RTGI 统计", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("当前 GI 模式", stats.currentGIMode.ToString());
                    EditorGUILayout.LabelField("RTAS 可用", stats.rtasAvailable ? "是" : "否");
                    if (stats.currentGIMode != IndirectDiffuseMode.ScreenSpace)
                    {
                        EditorGUILayout.LabelField("RTGI 光线数量", stats.rtgiRayCount.ToString("N0"));
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("SSGI 未激活", MessageType.Info);
                }
            }
        }
    }
}
