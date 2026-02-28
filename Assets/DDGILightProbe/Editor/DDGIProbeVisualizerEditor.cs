using UnityEngine;
using UnityEditor;

namespace DDGI
{

    [CustomEditor(typeof(DDGIProbeVisualizer))]
    public class DDGIProbeVisualizerEditor : UnityEditor.Editor
    {
        private SerializedProperty m_EnableVisualization;
        private SerializedProperty m_VisualizationMode;
        private SerializedProperty m_ProbeRadius;
        private SerializedProperty m_Intensity;
        private SerializedProperty m_SphereMesh;
        private SerializedProperty m_VisualizationMaterial;
        private SerializedProperty m_ActiveColor;
        private SerializedProperty m_InactiveColor;
        private SerializedProperty m_SleepingColor;

        private DDGIProbeVisualizer Visualizer => (DDGIProbeVisualizer)target;

        private bool m_ShowStateColors;
        private bool m_ShowResources;

        private void OnEnable()
        {
            m_EnableVisualization = serializedObject.FindProperty("m_EnableVisualization");
            m_VisualizationMode = serializedObject.FindProperty("m_VisualizationMode");
            m_ProbeRadius = serializedObject.FindProperty("m_ProbeRadius");
            m_Intensity = serializedObject.FindProperty("m_Intensity");
            m_SphereMesh = serializedObject.FindProperty("m_SphereMesh");
            m_VisualizationMaterial = serializedObject.FindProperty("m_VisualizationMaterial");
            m_ActiveColor = serializedObject.FindProperty("m_ActiveColor");
            m_InactiveColor = serializedObject.FindProperty("m_InactiveColor");
            m_SleepingColor = serializedObject.FindProperty("m_SleepingColor");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("DDGI Probe Visualizer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(m_EnableVisualization, new GUIContent("启用可视化"));

            EditorGUI.BeginDisabledGroup(!m_EnableVisualization.boolValue);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("显示设置", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_VisualizationMode, new GUIContent("可视化模式"));

            var mode = (ProbeVisualizationMode)m_VisualizationMode.intValue;
            EditorGUILayout.HelpBox(GetVisualizationModeDescription(mode), MessageType.Info);

            if (Visualizer.RequiresRaytracingData)
            {
                var updater = Visualizer.GetComponent<DDGIProbeUpdater>();
                if (updater == null || !updater.IsUsingRaytracing)
                {
                    EditorGUILayout.HelpBox(
                        "当前可视化模式需要Raytracing数据。请确保DDGIProbeUpdater已切换到Raytracing模式并正常运行。",
                        MessageType.Warning);
                }
            }

            EditorGUILayout.PropertyField(m_ProbeRadius, new GUIContent("探针半径"));
            EditorGUILayout.PropertyField(m_Intensity, new GUIContent("亮度强度"));

            EditorGUILayout.Space();
            m_ShowStateColors = EditorGUILayout.Foldout(m_ShowStateColors, "状态颜色", true);
            if (m_ShowStateColors)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_ActiveColor, new GUIContent("活跃状态"));
                EditorGUILayout.PropertyField(m_InactiveColor, new GUIContent("非活跃状态"));
                EditorGUILayout.PropertyField(m_SleepingColor, new GUIContent("休眠状态"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();
            m_ShowResources = EditorGUILayout.Foldout(m_ShowResources, "资源", true);
            if (m_ShowResources)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_SphereMesh, new GUIContent("球体网格"));
                EditorGUILayout.PropertyField(m_VisualizationMaterial, new GUIContent("可视化材质"));
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            if (GUILayout.Button("刷新可视化"))
            {
                Visualizer.RefreshVisualization();
                SceneView.RepaintAll();
            }

            serializedObject.ApplyModifiedProperties();

            if (GUI.changed)
            {
                SceneView.RepaintAll();
            }
        }

        private static string GetVisualizationModeDescription(ProbeVisualizationMode mode)
        {
            switch (mode)
            {
                case ProbeVisualizationMode.Irradiance:
                    return "从Irradiance Atlas采样，显示蒙特卡洛积分后的辐照度。\n"
                         + "球面每个方向显示该法线方向的入射Irradiance（经RTXGI gamma解码）。\n"
                         + "数据来源：Phase 3 积分写入的Atlas。";

                case ProbeVisualizationMode.Distance:
                    return "从Distance Atlas采样，显示各方向到最近表面的平均距离。\n"
                         + "蓝色=近距离，黄色=远距离（归一化至50m）。\n"
                         + "用于验证距离场和切比雪夫可见性测试的正确性。";

                case ProbeVisualizationMode.Normal:
                    return "显示球面法线方向，RGB对应XYZ（经 n*0.5+0.5 映射到[0,1]）。\n"
                         + "用于调试八面体映射和mesh法线方向是否正确。";

                case ProbeVisualizationMode.State:
                    return "显示探针状态颜色：蓝色=活跃，灰色=非活跃，黄色=休眠。\n"
                         + "颜色可在下方「状态颜色」栏自定义。";

                case ProbeVisualizationMode.HitPosition:
                    return "[Phase 1 G-Buffer] 光线命中点距离（颜色编码）。\n"
                         + "红色=近距离，蓝紫色=远距离（归一化至50m），深蓝=Miss。\n"
                         + "数据来源：_GBuffer_PositionDistance.w（hitDistance）。";

                case ProbeVisualizationMode.HitNormal:
                    return "[Phase 1 G-Buffer] 光线命中点的世界空间法线。\n"
                         + "RGB对应XYZ（经 n*0.5+0.5 映射），深蓝=Miss。\n"
                         + "数据来源：_GBuffer_NormalHitFlag.xyz。";

                case ProbeVisualizationMode.HitAlbedo:
                    return "[Phase 1 G-Buffer] 光线命中点的表面反照率（Albedo）。\n"
                         + "直接显示材质原始颜色，深蓝=Miss。\n"
                         + "数据来源：_GBuffer_AlbedoRoughness.rgb。";

                case ProbeVisualizationMode.HitEmission:
                    return "[Phase 1 G-Buffer] 光线命中点的自发光颜色。\n"
                         + "Hit时显示材质Emission，Miss时显示天空盒采样结果。\n"
                         + "受亮度强度（Intensity）参数影响。\n"
                         + "数据来源：_GBuffer_EmissionMetallic.rgb。";

                case ProbeVisualizationMode.HitDistance:
                    return "[Phase 1 G-Buffer] 光线命中距离（五段热力图）。\n"
                         + "蓝→青→绿→黄→红（归一化至30m），紫色=Miss。\n"
                         + "数据来源：_GBuffer_PositionDistance.w。";

                case ProbeVisualizationMode.DirectIrradiance:
                    return "[Phase 2-1 延迟光照] 命中点接收的直接光入射Irradiance。\n"
                         + "公式：E_direct = Σ(L_light · cosθ · shadow)\n"
                         + "不含表面Albedo，用于验证光源方向和阴影是否正确。\n"
                         + "数据来源：DDGIDeferredLighting.compute。";

                case ProbeVisualizationMode.IndirectIrradiance:
                    return "[Phase 2-2 间接光采样] 命中点从上一帧DDGI Atlas采样的间接光Irradiance。\n"
                         + "通过三线性插值+切比雪夫可见性加权，从周围8个Probe采样。\n"
                         + "不含表面Albedo，用于验证间接光传递是否正确。\n"
                         + "数据来源：DDGIIndirectLighting.compute。";

                case ProbeVisualizationMode.OutgoingRadiance:
                    return "[Phase 2-3 Radiance合成] 命中点反射回Probe方向的出射Radiance。\n"
                         + "公式：L_o = L_e + (albedo/π) × (E_direct + E_indirect)\n"
                         + "这是最终输入蒙特卡洛积分的数据。\n"
                         + "Miss时直接使用RayGen写入的天空盒采样结果（G-Buffer emission）。\n"
                         + "数据来源：DDGIRadianceComposite.compute。";

                case ProbeVisualizationMode.ProbeOffset:
                    return "[Probe Relocation] 显示探针偏移量。\n"
                         + "颜色编码偏移方向，亮度编码偏移大小。\n"
                         + "偏移量存储在ProbeData.xyz中。";

                case ProbeVisualizationMode.ProbeRelocationState:
                    return "[Probe Relocation] 显示探针重定位状态。\n"
                         + "正常=绿色，在几何体内部=红色，靠近表面=黄色。";

                case ProbeVisualizationMode.ProbeClassificationState:
                    return "[Probe Classification] 显示探针分类状态。\n"
                         + "绿色=ACTIVE（活跃，正常更新和采样）\n"
                         + "红色=INACTIVE（非活跃，跳过更新和采样）\n"
                         + "INACTIVE探针：完全在几何体内部或附近无几何体。\n"
                         + "数据来源：ProbeData.w（0=ACTIVE, 1=INACTIVE）。";

                case ProbeVisualizationMode.ProbeVariability:
                    return "[Probe Variability] 显示探针变异度热力图。\n"
                         + "蓝色=完全稳定（CV≈0），红色=剧烈变化（CV≥0.1）\n"
                         + "变异系数公式：CV = σ / μ\n"
                         + "使用Welford's算法增量计算方差。\n"
                         + "需要启用ProbeVariability功能。";

                default:
                    return "未知的可视化模式。";
            }
        }
    }
}
