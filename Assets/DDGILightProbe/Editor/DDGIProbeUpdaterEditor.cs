using UnityEngine;
using UnityEditor;

namespace DDGI
{

    [CustomEditor(typeof(DDGIProbeUpdater))]
    public class DDGIProbeUpdaterEditor : UnityEditor.Editor
    {

        private SerializedProperty m_EnableUpdate;
        private SerializedProperty m_UpdateMode;
        private SerializedProperty m_RayGenShader;
        private SerializedProperty m_UpdatesPerFrame;

        private SerializedProperty m_RaysPerProbe;
        private SerializedProperty m_RayMinDistance;
        private SerializedProperty m_RayMaxDistance;

        private SerializedProperty m_DeferredLightingShader;
        private SerializedProperty m_IndirectLightingShader;
        private SerializedProperty m_RadianceCompositeShader;
        private SerializedProperty m_MonteCarloIntegrationShader;
        private SerializedProperty m_ProbeRelocationShader;
        private SerializedProperty m_ProbeClassificationShader;
        private SerializedProperty m_VariabilityReductionShader;

        private SerializedProperty m_SkyboxIntensity;
        private SerializedProperty m_IndirectIntensity;
        private SerializedProperty m_NormalBias;
        private SerializedProperty m_ChebyshevBias;

        private SerializedProperty m_ShadowStrength;
        private SerializedProperty m_ShadowBias;
        private SerializedProperty m_ShadowNormalBias;

        private DDGIProbeUpdater Updater => (DDGIProbeUpdater)target;

        private bool m_ShowRaytracingSettings = true;
        private bool m_ShowLightingSettings = true;
        private bool m_ShowShadowSettings = true;
        private bool m_ShowDebugInfo = false;

        private void OnEnable()
        {

            m_EnableUpdate = serializedObject.FindProperty("m_EnableUpdate");
            m_UpdateMode = serializedObject.FindProperty("m_UpdateMode");
            m_RayGenShader = serializedObject.FindProperty("m_RayGenShader");
            m_UpdatesPerFrame = serializedObject.FindProperty("m_UpdatesPerFrame");

            m_RaysPerProbe = serializedObject.FindProperty("m_RaysPerProbe");
            m_RayMinDistance = serializedObject.FindProperty("m_RayMinDistance");
            m_RayMaxDistance = serializedObject.FindProperty("m_RayMaxDistance");

            m_DeferredLightingShader = serializedObject.FindProperty("m_DeferredLightingShader");
            m_IndirectLightingShader = serializedObject.FindProperty("m_IndirectLightingShader");
            m_RadianceCompositeShader = serializedObject.FindProperty("m_RadianceCompositeShader");
            m_MonteCarloIntegrationShader = serializedObject.FindProperty("m_MonteCarloIntegrationShader");
            m_ProbeRelocationShader = serializedObject.FindProperty("m_ProbeRelocationShader");
            m_ProbeClassificationShader = serializedObject.FindProperty("m_ProbeClassificationShader");
            m_VariabilityReductionShader = serializedObject.FindProperty("m_VariabilityReductionShader");

            m_SkyboxIntensity = serializedObject.FindProperty("m_SkyboxIntensity");
            m_IndirectIntensity = serializedObject.FindProperty("m_IndirectIntensity");
            m_NormalBias = serializedObject.FindProperty("m_NormalBias");
            m_ChebyshevBias = serializedObject.FindProperty("m_ChebyshevBias");

            m_ShadowStrength = serializedObject.FindProperty("m_ShadowStrength");
            m_ShadowBias = serializedObject.FindProperty("m_ShadowBias");
            m_ShadowNormalBias = serializedObject.FindProperty("m_ShadowNormalBias");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("DDGI Probe Updater", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawStatusInfo();

            EditorGUILayout.Space();

            DrawUpdateSettings();

            if (m_UpdateMode.enumValueIndex == (int)ProbeUpdateMode.Raytracing)
            {
                DrawRaytracingSettings();
            }

            DrawLightingSettings();

            DrawShadowSettings();

            DrawOperationButtons();

            DrawDebugInfo();

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawStatusInfo()
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("已初始化:", GUILayout.Width(80));
                EditorGUILayout.LabelField(Updater.IsInitialized ? "是" : "否");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("DXR支持:", GUILayout.Width(80));
                EditorGUILayout.LabelField(Updater.IsRaytracingSupported ? "是" : "否");
            }

            if (Updater.IsUsingRaytracing)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("当前模式:", GUILayout.Width(80));
                    EditorGUILayout.LabelField("Raytracing", EditorStyles.boldLabel);
                }
            }
        }

        private void DrawUpdateSettings()
        {
            EditorGUILayout.LabelField("更新设置", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_EnableUpdate, new GUIContent("启用更新"));

            EditorGUILayout.PropertyField(m_UpdateMode, new GUIContent("更新模式"));
            EditorGUILayout.PropertyField(m_UpdatesPerFrame, new GUIContent("每帧更新次数"));
            EditorGUILayout.Space();

            if (m_UpdateMode.enumValueIndex == (int)ProbeUpdateMode.ComputeShader)
            {
            }
            else
            {
                EditorGUILayout.PropertyField(m_RayGenShader, new GUIContent("RayGen Shader"));

                if (m_RayGenShader.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox("请指定DDGIRayGen RayTracing Shader", MessageType.Warning);

                    if (GUILayout.Button("自动查找Shader"))
                    {
                        string[] guids = AssetDatabase.FindAssets("DDGIRayGen t:RayTracingShader");
                        if (guids.Length > 0)
                        {
                            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                            m_RayGenShader.objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.Experimental.Rendering.RayTracingShader>(path);
                        }
                    }
                }

                if (!Updater.IsRaytracingSupported)
                {
                    EditorGUILayout.HelpBox("当前设备不支持DXR光线追踪，将自动回退到ComputeShader模式", MessageType.Warning);
                }
            }
        }

        private void DrawRaytracingSettings()
        {
            EditorGUILayout.Space();
            m_ShowRaytracingSettings = EditorGUILayout.Foldout(m_ShowRaytracingSettings, "Raytracing设置", true);

            if (m_ShowRaytracingSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(m_RaysPerProbe, new GUIContent("每Probe光线数"));
                EditorGUILayout.PropertyField(m_RayMinDistance, new GUIContent("最小光线距离"));
                EditorGUILayout.PropertyField(m_RayMaxDistance, new GUIContent("最大光线距离"));

                EditorGUILayout.Space();
                EditorGUILayout.PropertyField(m_DeferredLightingShader, new GUIContent("延迟光照Shader"));
                EditorGUILayout.PropertyField(m_IndirectLightingShader, new GUIContent("间接光采样Shader"));
                EditorGUILayout.PropertyField(m_RadianceCompositeShader, new GUIContent("Radiance合成Shader"));
                EditorGUILayout.PropertyField(m_MonteCarloIntegrationShader, new GUIContent("蒙特卡洛积分Shader"));
                EditorGUILayout.PropertyField(m_ProbeRelocationShader, new GUIContent("Relocation Shader"));
                EditorGUILayout.PropertyField(m_ProbeClassificationShader, new GUIContent("Classification Shader"));
                EditorGUILayout.PropertyField(m_VariabilityReductionShader, new GUIContent("Variability归约Shader"));

                if (m_DeferredLightingShader.objectReferenceValue == null ||
                    m_IndirectLightingShader.objectReferenceValue == null ||
                    m_RadianceCompositeShader.objectReferenceValue == null ||
                    m_MonteCarloIntegrationShader.objectReferenceValue == null ||
                    m_ProbeRelocationShader.objectReferenceValue == null ||
                    m_ProbeClassificationShader.objectReferenceValue == null ||
                    m_VariabilityReductionShader.objectReferenceValue == null)
                {
                    if (GUILayout.Button("自动查找Shaders"))
                    {
                        FindAndAssignShader("DDGIDeferredLighting", m_DeferredLightingShader);
                        FindAndAssignShader("DDGIIndirectLighting", m_IndirectLightingShader);
                        FindAndAssignShader("DDGIRadianceComposite", m_RadianceCompositeShader);
                        FindAndAssignShader("DDGIMonteCarloIntegration", m_MonteCarloIntegrationShader);
                        FindAndAssignShader("DDGIProbeRelocation", m_ProbeRelocationShader);
                        FindAndAssignShader("DDGIProbeClassification", m_ProbeClassificationShader);
                        FindAndAssignShader("DDGIVariabilityReduction", m_VariabilityReductionShader);
                    }
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawLightingSettings()
        {
            EditorGUILayout.Space();
            m_ShowLightingSettings = EditorGUILayout.Foldout(m_ShowLightingSettings, "光照设置", true);

            if (m_ShowLightingSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(m_SkyboxIntensity, new GUIContent("天空盒强度"));
                EditorGUILayout.PropertyField(m_IndirectIntensity, new GUIContent("间接光强度"));
                EditorGUILayout.PropertyField(m_NormalBias, new GUIContent("法线偏移"));
                EditorGUILayout.PropertyField(m_ChebyshevBias, new GUIContent("切比雪夫偏移"));

                EditorGUI.indentLevel--;
            }
        }

        private void DrawShadowSettings()
        {
            EditorGUILayout.Space();
            m_ShowShadowSettings = EditorGUILayout.Foldout(m_ShowShadowSettings, "阴影设置", true);

            if (m_ShowShadowSettings)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(m_ShadowStrength, new GUIContent("阴影强度"));
                EditorGUILayout.PropertyField(m_ShadowBias, new GUIContent("深度偏移"));
                EditorGUILayout.PropertyField(m_ShadowNormalBias, new GUIContent("法线偏移"));

                EditorGUI.indentLevel--;
            }
        }

        private void DrawOperationButtons()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(!Updater.IsInitialized);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("强制更新"))
                {
                    Updater.ForceUpdate();
                    SceneView.RepaintAll();
                }
            }

            EditorGUI.EndDisabledGroup();

            if (!Updater.IsInitialized)
            {
                if (GUILayout.Button("初始化"))
                {
                    Updater.enabled = false;
                    Updater.enabled = true;
                }
            }
        }

        private void DrawDebugInfo()
        {
            EditorGUILayout.Space();
            m_ShowDebugInfo = EditorGUILayout.Foldout(m_ShowDebugInfo, "调试信息", true);

            if (m_ShowDebugInfo && Updater.IsUsingRaytracing && Updater.RaytracingManager != null)
            {
                EditorGUI.indentLevel++;

                var rtManager = Updater.RaytracingManager;
                EditorGUILayout.LabelField($"G-Buffer尺寸: {rtManager.GetGBufferSize()}");
                EditorGUILayout.LabelField($"总光线数: {rtManager.GetTotalRayCount()}");
                EditorGUILayout.LabelField($"每Probe光线数: {rtManager.RaysPerProbe}");

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Variability信息", EditorStyles.boldLabel);

                float variability = rtManager.CurrentGlobalVariability;
                EditorGUILayout.LabelField($"当前全局变异度: {variability:F4}");

                int updateInterval = rtManager.CurrentUpdateInterval;
                EditorGUILayout.LabelField($"当前更新间隔: {updateInterval} 帧");

                EditorGUILayout.Space();
                Rect progressRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                EditorGUI.ProgressBar(progressRect, Mathf.Clamp01(variability * 10f), $"变异度: {variability:P1}");

                if (Application.isPlaying)
                {
                    Repaint();
                }

                EditorGUI.indentLevel--;
            }
        }

        private void FindAndAssignShader(string shaderName, SerializedProperty property)
        {
            string[] guids = AssetDatabase.FindAssets($"{shaderName} t:ComputeShader");
            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                property.objectReferenceValue = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
            }
        }
    }
}
