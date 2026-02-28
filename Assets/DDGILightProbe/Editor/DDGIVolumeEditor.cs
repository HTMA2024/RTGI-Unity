using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

namespace DDGI
{

    [CustomEditor(typeof(DDGIVolume))]
    public class DDGIVolumeEditor : UnityEditor.Editor
    {
        #region 常量和静态字段

        private static readonly Color ProbeColor = new Color(0.3f, 0.7f, 1.0f, 0.8f);
        private static readonly Color ProbeInactiveColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        private static readonly Color ProbeSleepingColor = new Color(1.0f, 0.8f, 0.2f, 0.8f);
        private static readonly Color ProbeSelectedColor = new Color(1.0f, 0.4f, 0.1f, 1.0f);
        private static readonly Color BoundsColor = new Color(0.2f, 0.8f, 0.2f, 0.8f);
        private static readonly Color GridLineColor = new Color(0.5f, 0.5f, 0.5f, 0.3f);
        private static readonly Color HandleColor = new Color(0.2f, 0.8f, 0.2f, 1.0f);

        private const float DefaultProbeRadius = 0.15f;
        private const float SelectedProbeRadius = 0.2f;

        #endregion

        #region SerializedProperties

        private SerializedProperty m_DescriptorProperty;
        private SerializedProperty m_EditModeProperty;
        private SerializedProperty m_ProbeSpacingProperty;
        private SerializedProperty m_ProbeCountsProperty;
        private SerializedProperty m_VolumeSizeProperty;
        private SerializedProperty m_HysteresisProperty;
        private SerializedProperty m_IrradianceGammaProperty;
        private SerializedProperty m_IrradianceThresholdProperty;
        private SerializedProperty m_BrightnessThresholdProperty;
        private SerializedProperty m_ViewBiasProperty;

        private SerializedProperty m_AtlasConfigProperty;
        private SerializedProperty m_IrradianceProbeResolutionProperty;
        private SerializedProperty m_DistanceProbeResolutionProperty;
        private SerializedProperty m_GutterSizeProperty;

        private SerializedProperty m_EnableProbeRelocationProperty;
        private SerializedProperty m_ProbeMinFrontfaceDistanceProperty;
        private SerializedProperty m_ProbeBackfaceThresholdProperty;
        private SerializedProperty m_RelocationUpdateIntervalProperty;

        private SerializedProperty m_EnableProbeClassificationProperty;
        private SerializedProperty m_ClassificationUpdateIntervalProperty;

        private SerializedProperty m_EnableProbeVariabilityProperty;
        private SerializedProperty m_EnableAdaptiveUpdateProperty;
        private SerializedProperty m_LowVariabilityThresholdProperty;
        private SerializedProperty m_HighVariabilityThresholdProperty;
        private SerializedProperty m_MinUpdateIntervalProperty;
        private SerializedProperty m_MaxUpdateIntervalProperty;

        #endregion

        #region 编辑器状态

        private int m_SelectedProbeIndex = -1;
        private bool m_ShowProbeIndices;
        private bool m_ShowGridLines = false;
        private bool m_ShowProbes = false;
        private float m_ProbeGizmoSize = 0.25f;

        private bool m_ShowGizmoSettings = true;
        private bool m_ShowStatistics = true;
        private bool m_ShowAtlasInfo;
        private bool m_ShowSelectedProbe;

        private BoxBoundsHandle m_BoundsHandle;

        #endregion

        private DDGIVolume Volume => (DDGIVolume)target;

        private void OnEnable()
        {
            m_DescriptorProperty = serializedObject.FindProperty("m_Descriptor");
            m_EditModeProperty = m_DescriptorProperty.FindPropertyRelative("editMode");
            m_ProbeSpacingProperty = m_DescriptorProperty.FindPropertyRelative("probeSpacing");
            m_ProbeCountsProperty = m_DescriptorProperty.FindPropertyRelative("probeCounts");
            m_VolumeSizeProperty = m_DescriptorProperty.FindPropertyRelative("volumeSize");
            m_HysteresisProperty = m_DescriptorProperty.FindPropertyRelative("hysteresis");
            m_IrradianceGammaProperty = m_DescriptorProperty.FindPropertyRelative("irradianceGamma");
            m_IrradianceThresholdProperty = m_DescriptorProperty.FindPropertyRelative("irradianceThreshold");
            m_BrightnessThresholdProperty = m_DescriptorProperty.FindPropertyRelative("brightnessThreshold");
            m_ViewBiasProperty = m_DescriptorProperty.FindPropertyRelative("viewBias");

            m_EnableProbeRelocationProperty = m_DescriptorProperty.FindPropertyRelative("enableProbeRelocation");
            m_ProbeMinFrontfaceDistanceProperty = m_DescriptorProperty.FindPropertyRelative("probeMinFrontfaceDistance");
            m_ProbeBackfaceThresholdProperty = m_DescriptorProperty.FindPropertyRelative("probeBackfaceThreshold");
            m_RelocationUpdateIntervalProperty = m_DescriptorProperty.FindPropertyRelative("relocationUpdateInterval");

            m_EnableProbeClassificationProperty = m_DescriptorProperty.FindPropertyRelative("enableProbeClassification");
            m_ClassificationUpdateIntervalProperty = m_DescriptorProperty.FindPropertyRelative("classificationUpdateInterval");

            m_EnableProbeVariabilityProperty = m_DescriptorProperty.FindPropertyRelative("enableProbeVariability");
            m_EnableAdaptiveUpdateProperty = m_DescriptorProperty.FindPropertyRelative("enableAdaptiveUpdate");
            m_LowVariabilityThresholdProperty = m_DescriptorProperty.FindPropertyRelative("lowVariabilityThreshold");
            m_HighVariabilityThresholdProperty = m_DescriptorProperty.FindPropertyRelative("highVariabilityThreshold");
            m_MinUpdateIntervalProperty = m_DescriptorProperty.FindPropertyRelative("minUpdateInterval");
            m_MaxUpdateIntervalProperty = m_DescriptorProperty.FindPropertyRelative("maxUpdateInterval");

            m_AtlasConfigProperty = serializedObject.FindProperty("m_AtlasConfig");
            m_IrradianceProbeResolutionProperty = m_AtlasConfigProperty.FindPropertyRelative("irradianceProbeResolution");
            m_DistanceProbeResolutionProperty = m_AtlasConfigProperty.FindPropertyRelative("distanceProbeResolution");
            m_GutterSizeProperty = m_AtlasConfigProperty.FindPropertyRelative("gutterSize");

            m_BoundsHandle = new BoxBoundsHandle
            {
                handleColor = HandleColor,
                wireframeColor = BoundsColor
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space();
            DrawHeader();
            EditorGUILayout.Space();

            Vector3 prevSpacing = m_ProbeSpacingProperty.vector3Value;
            Vector3Int prevCounts = m_ProbeCountsProperty.vector3IntValue;
            Vector3 prevSize = m_VolumeSizeProperty.vector3Value;
            int prevEditMode = m_EditModeProperty.enumValueIndex;

            DrawEditModeSection();
            EditorGUILayout.Space();

            DrawAtlasConfigSection();
            EditorGUILayout.Space();

            DrawUpdateParametersSection();
            EditorGUILayout.Space();

            DrawProbeRelocationSection();
            EditorGUILayout.Space();

            DrawProbeClassificationSection();
            EditorGUILayout.Space();

            DrawProbeVariabilitySection();
            EditorGUILayout.Space();

            DrawGizmoSettings();
            EditorGUILayout.Space();

            DrawStatistics();
            EditorGUILayout.Space();

            DrawAtlasInfo();

            DrawSelectedProbeInfo();

            DrawButtons();

            bool layoutChanged = prevSpacing != m_ProbeSpacingProperty.vector3Value ||
                                 prevCounts != m_ProbeCountsProperty.vector3IntValue ||
                                 prevSize != m_VolumeSizeProperty.vector3Value ||
                                 prevEditMode != m_EditModeProperty.enumValueIndex;

            if (serializedObject.ApplyModifiedProperties() && layoutChanged)
            {

                Volume.RebuildProbes();
            }

            if (GUI.changed)
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("DDGI Volume", EditorStyles.boldLabel);

            if (GUILayout.Button("适配场景", GUILayout.Width(70)))
            {
                FitToSceneRenderers();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void FitToSceneRenderers()
        {
            Bounds sceneBounds = CalculateSceneRenderersBounds();

            if (sceneBounds.size == Vector3.zero)
            {
                EditorUtility.DisplayDialog("DDGI Volume", "场景中没有找到有效的Renderer", "确定");
                return;
            }

            Undo.RecordObject(Volume.transform, "Fit DDGI Volume to Scene");
            Undo.RecordObject(target, "Fit DDGI Volume to Scene");

            Vector3 newPosition = sceneBounds.min;
            Volume.transform.position = newPosition;

            Vector3 size = sceneBounds.size;
            m_VolumeSizeProperty.vector3Value = size;
            m_EditModeProperty.enumValueIndex = (int)VolumeEditMode.VolumeSizeAutoProbes;

            Vector3 spacing = m_ProbeSpacingProperty.vector3Value;
            Vector3Int probeCounts = new Vector3Int(
                Mathf.Max(2, Mathf.FloorToInt(size.x / spacing.x) + 1),
                Mathf.Max(2, Mathf.FloorToInt(size.y / spacing.y) + 1),
                Mathf.Max(2, Mathf.FloorToInt(size.z / spacing.z) + 1)
            );
            m_ProbeCountsProperty.vector3IntValue = probeCounts;

            serializedObject.ApplyModifiedProperties();
            Volume.RebuildProbes();

            SceneView.lastActiveSceneView?.Frame(sceneBounds, false);

            Debug.Log($"[DDGIVolume] 已适配场景边界: 位置={newPosition}, 尺寸={size}, 探针数={probeCounts}, 总计={probeCounts.x * probeCounts.y * probeCounts.z}个探针");
        }

        private Bounds CalculateSceneRenderersBounds()
        {
            Renderer[] renderers = FindObjectsOfType<Renderer>();

            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Bounds bounds = new Bounds();
            bool initialized = false;

            foreach (Renderer renderer in renderers)
            {

                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (renderer is ParticleSystemRenderer ||
                    renderer is TrailRenderer ||
                    renderer is LineRenderer)
                    continue;

                if (renderer.bounds.size == Vector3.zero)
                    continue;

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return bounds;
        }

        private void DrawAtlasConfigSection()
        {
            EditorGUILayout.LabelField("Atlas纹理配置", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_IrradianceProbeResolutionProperty, new GUIContent("辐照度探针分辨率（不含边框）"));
            EditorGUILayout.PropertyField(m_DistanceProbeResolutionProperty, new GUIContent("距离探针分辨率（不含边框）"));
            EditorGUILayout.PropertyField(m_GutterSizeProperty, new GUIContent("边框像素数（用于防止采样越界）"));
        }

        private void DrawEditModeSection()
        {
            EditorGUILayout.LabelField("空间布局", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_EditModeProperty, new GUIContent("编辑模式"));
            bool modeChanged = EditorGUI.EndChangeCheck();

            VolumeEditMode editMode = (VolumeEditMode)m_EditModeProperty.enumValueIndex;

            EditorGUILayout.Space(5);

            switch (editMode)
            {
                case VolumeEditMode.ProbeCountsAndSpacing:
                    DrawProbeCountsAndSpacingMode(modeChanged);
                    break;

                case VolumeEditMode.VolumeSizeAutoProbes:
                    DrawVolumeSizeAutoProbesMode(modeChanged);
                    break;

                case VolumeEditMode.VolumeSizeAutoSpacing:
                    DrawVolumeSizeAutoSpacingMode(modeChanged);
                    break;
            }
        }

        private void DrawProbeCountsAndSpacingMode(bool modeChanged)
        {

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_ProbeSpacingProperty, new GUIContent("探针间距"));
            bool spacingChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_ProbeCountsProperty, new GUIContent("探针数量"));
            bool countsChanged = EditorGUI.EndChangeCheck();

            Vector3Int probeCounts = m_ProbeCountsProperty.vector3IntValue;
            Vector3 spacing = m_ProbeSpacingProperty.vector3Value;
            Vector3 volumeSize = new Vector3(
                (probeCounts.x - 1) * spacing.x,
                (probeCounts.y - 1) * spacing.y,
                (probeCounts.z - 1) * spacing.z
            );

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Vector3Field("Volume尺寸（自动计算）", volumeSize);
            EditorGUI.EndDisabledGroup();

            if (countsChanged || spacingChanged || modeChanged)
            {
                m_VolumeSizeProperty.vector3Value = volumeSize;
            }

            EditorGUILayout.HelpBox(
                $"总探针数: {probeCounts.x * probeCounts.y * probeCounts.z}",
                MessageType.Info);
        }

        private void DrawVolumeSizeAutoProbesMode(bool modeChanged)
        {

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_ProbeSpacingProperty, new GUIContent("探针间距"));
            bool spacingChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_VolumeSizeProperty, new GUIContent("Volume尺寸"));
            bool sizeChanged = EditorGUI.EndChangeCheck();

            Vector3 size = m_VolumeSizeProperty.vector3Value;
            Vector3 spacing = m_ProbeSpacingProperty.vector3Value;
            Vector3Int probeCounts = new Vector3Int(
                Mathf.Max(2, Mathf.FloorToInt(size.x / spacing.x) + 1),
                Mathf.Max(2, Mathf.FloorToInt(size.y / spacing.y) + 1),
                Mathf.Max(2, Mathf.FloorToInt(size.z / spacing.z) + 1)
            );

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Vector3IntField("探针数量（自动计算）", probeCounts);
            EditorGUI.EndDisabledGroup();

            if (sizeChanged || spacingChanged || modeChanged)
            {
                m_ProbeCountsProperty.vector3IntValue = probeCounts;
            }

            Vector3 actualSize = new Vector3(
                (probeCounts.x - 1) * spacing.x,
                (probeCounts.y - 1) * spacing.y,
                (probeCounts.z - 1) * spacing.z
            );
            EditorGUILayout.HelpBox(
                $"实际覆盖: {actualSize.x:F1}m × {actualSize.y:F1}m × {actualSize.z:F1}m\n" +
                $"总探针数: {probeCounts.x * probeCounts.y * probeCounts.z}",
                MessageType.Info);
        }

        private void DrawVolumeSizeAutoSpacingMode(bool modeChanged)
        {

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_VolumeSizeProperty, new GUIContent("Volume尺寸"));
            bool sizeChanged = EditorGUI.EndChangeCheck();

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(m_ProbeCountsProperty, new GUIContent("探针数量"));
            bool countsChanged = EditorGUI.EndChangeCheck();

            Vector3 size = m_VolumeSizeProperty.vector3Value;
            Vector3Int probeCounts = m_ProbeCountsProperty.vector3IntValue;

            probeCounts = new Vector3Int(
                Mathf.Max(2, probeCounts.x),
                Mathf.Max(2, probeCounts.y),
                Mathf.Max(2, probeCounts.z)
            );

            Vector3 spacing = new Vector3(
                size.x / (probeCounts.x - 1),
                size.y / (probeCounts.y - 1),
                size.z / (probeCounts.z - 1)
            );

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Vector3Field("探针间距（自动计算）", spacing);
            EditorGUI.EndDisabledGroup();

            if (sizeChanged || countsChanged || modeChanged)
            {
                m_ProbeSpacingProperty.vector3Value = spacing;
                m_ProbeCountsProperty.vector3IntValue = probeCounts;
            }

            EditorGUILayout.HelpBox(
                $"间距: {spacing.x:F2}m × {spacing.y:F2}m × {spacing.z:F2}m\n" +
                $"总探针数: {probeCounts.x * probeCounts.y * probeCounts.z}",
                MessageType.Info);
        }

        private void DrawUpdateParametersSection()
        {
            EditorGUILayout.LabelField("更新参数", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_HysteresisProperty, new GUIContent("时间滞后系数"));
            EditorGUILayout.PropertyField(m_IrradianceGammaProperty, new GUIContent("Irradiance Gamma"));
            EditorGUILayout.PropertyField(m_IrradianceThresholdProperty, new GUIContent("辐照度阈值"));
            EditorGUILayout.PropertyField(m_BrightnessThresholdProperty, new GUIContent("亮度阈值"));
            EditorGUILayout.PropertyField(m_ViewBiasProperty, new GUIContent("视线偏移"));
        }

        private void DrawProbeRelocationSection()
        {
            EditorGUILayout.LabelField("Probe Relocation", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_EnableProbeRelocationProperty, new GUIContent("启用探针重定位",
                "自动检测并移动陷入几何体内部或过于靠近表面的探针"));

            if (m_EnableProbeRelocationProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_ProbeMinFrontfaceDistanceProperty, new GUIContent("最小前面距离",
                    "探针与最近表面的最小允许距离（米）"));
                EditorGUILayout.PropertyField(m_ProbeBackfaceThresholdProperty, new GUIContent("背面阈值",
                    "背面命中比例超过此值时认为探针在几何体内部"));
                EditorGUILayout.PropertyField(m_RelocationUpdateIntervalProperty, new GUIContent("更新间隔（帧）",
                    "每隔N帧更新一次重定位，0表示每帧更新"));

                EditorGUILayout.HelpBox(
                    "Probe Relocation会自动检测陷入墙壁或过于靠近表面的探针，并将其移动到合适位置。\n" +
                    "偏移量被限制在探针间距的45%以内。",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawProbeClassificationSection()
        {
            EditorGUILayout.LabelField("Probe Classification", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_EnableProbeClassificationProperty, new GUIContent("启用探针分类",
                "自动标记无效探针（完全在几何体内部或附近无几何体）"));

            if (m_EnableProbeClassificationProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(m_ClassificationUpdateIntervalProperty, new GUIContent("更新间隔（帧）",
                    "每隔N帧更新一次分类，0表示每帧更新"));

                EditorGUILayout.HelpBox(
                    "Probe Classification会自动识别无效探针并标记为INACTIVE状态：\n" +
                    "• 完全在几何体内部的探针（背面命中比例超过阈值）\n" +
                    "• 附近没有几何体的探针（所有命中点都在体素边界外）\n" +
                    "INACTIVE探针会被跳过更新和采样，提升性能。",
                    MessageType.Info);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawProbeVariabilitySection()
        {
            EditorGUILayout.LabelField("Probe Variability", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(m_EnableProbeVariabilityProperty, new GUIContent("启用变异度计算",
                "计算每个探针texel的变异系数(CV)，用于自适应更新"));

            if (m_EnableProbeVariabilityProperty.boolValue)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(m_EnableAdaptiveUpdateProperty, new GUIContent("启用自适应更新",
                    "基于Variability动态调整更新频率"));

                if (m_EnableAdaptiveUpdateProperty.boolValue)
                {
                    EditorGUILayout.PropertyField(m_LowVariabilityThresholdProperty, new GUIContent("低变异度阈值",
                        "低于此值认为场景稳定，可降低更新频率"));
                    EditorGUILayout.PropertyField(m_HighVariabilityThresholdProperty, new GUIContent("高变异度阈值",
                        "高于此值认为场景剧烈变化，使用最高更新频率"));
                    EditorGUILayout.PropertyField(m_MinUpdateIntervalProperty, new GUIContent("最小更新间隔（帧）",
                        "场景变化剧烈时使用的更新间隔"));
                    EditorGUILayout.PropertyField(m_MaxUpdateIntervalProperty, new GUIContent("最大更新间隔（帧）",
                        "场景稳定时使用的更新间隔"));
                }

                EditorGUILayout.HelpBox(
                    "Probe Variability使用变异系数(CV = σ/μ)衡量场景变化程度：\n" +
                    "• CV ≈ 0：场景完全稳定，可跳过更新\n" +
                    "• CV > 阈值：场景变化剧烈，需频繁更新\n" +
                    "• 自适应更新可根据场景稳定性动态调整更新频率\n" +
                    "可视化模式：ProbeVariability（热力图显示）",
                    MessageType.Info);

                EditorGUI.indentLevel--;
            }
        }

        private void DrawGizmoSettings()
        {
            m_ShowGizmoSettings = EditorGUILayout.Foldout(m_ShowGizmoSettings, "Gizmo 设置", true);
            if (!m_ShowGizmoSettings) return;

            EditorGUI.indentLevel++;
            m_ShowProbes = EditorGUILayout.Toggle("显示探针", m_ShowProbes);
            m_ProbeGizmoSize = EditorGUILayout.Slider("探针大小", m_ProbeGizmoSize, 0.1f, 3.0f);
            m_ShowProbeIndices = EditorGUILayout.Toggle("显示探针索引", m_ShowProbeIndices);
            m_ShowGridLines = EditorGUILayout.Toggle("显示网格线", m_ShowGridLines);
            EditorGUI.indentLevel--;
        }

        private void DrawStatistics()
        {
            m_ShowStatistics = EditorGUILayout.Foldout(m_ShowStatistics, "统计信息", true);
            if (!m_ShowStatistics) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"探针总数: {Volume.ProbeCount}");
            EditorGUILayout.LabelField($"已初始化: {Volume.IsInitialized}");
            EditorGUI.indentLevel--;
        }

        private void DrawAtlasInfo()
        {
            if (Volume.AtlasManager == null || !Volume.AtlasManager.IsInitialized)
                return;

            m_ShowAtlasInfo = EditorGUILayout.Foldout(m_ShowAtlasInfo, "Atlas 信息", true);
            if (!m_ShowAtlasInfo) return;

            EditorGUI.indentLevel++;

            var irradianceSize = Volume.AtlasManager.IrradianceAtlasSize;
            var distanceSize = Volume.AtlasManager.DistanceAtlasSize;

            EditorGUILayout.LabelField($"辐照度Atlas: {irradianceSize.x}x{irradianceSize.y}");
            EditorGUILayout.LabelField($"距离Atlas: {distanceSize.x}x{distanceSize.y}");
            EditorGUILayout.LabelField($"每行探针数: {Volume.AtlasManager.ProbesPerRow}");

            EditorGUILayout.Space();

            if (Volume.AtlasManager.IrradianceAtlas != null)
            {
                EditorGUILayout.LabelField("辐照度Atlas预览:");
                Rect rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, Volume.AtlasManager.IrradianceAtlas);
            }

            if (Volume.AtlasManager.DistanceAtlas != null)
            {
                EditorGUILayout.LabelField("距离Atlas预览:");
                Rect rect = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(rect, Volume.AtlasManager.DistanceAtlas);
            }

            EditorGUI.indentLevel--;
        }

        private void DrawSelectedProbeInfo()
        {
            if (m_SelectedProbeIndex < 0 || m_SelectedProbeIndex >= Volume.ProbeCount)
                return;

            var probe = Volume.GetProbe(m_SelectedProbeIndex);
            if (probe == null) return;

            m_ShowSelectedProbe = EditorGUILayout.Foldout(m_ShowSelectedProbe, "选中探针", true);
            if (!m_ShowSelectedProbe) return;

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField($"索引: {probe.flatIndex} ({probe.gridIndex})");
            EditorGUILayout.LabelField($"位置: {probe.position}");
            EditorGUILayout.LabelField($"状态: {probe.state}");
            EditorGUILayout.LabelField($"辐照度UV: {probe.irradianceAtlasUV}");
            EditorGUILayout.LabelField($"距离UV: {probe.distanceAtlasUV}");
            EditorGUI.indentLevel--;
        }

        private void DrawButtons()
        {
            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重建探针"))
            {
                Volume.RebuildProbes();
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("更新位置"))
            {
                Volume.UpdateProbePositions();
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重建Atlas"))
            {
                Volume.RebuildAtlas();
                SceneView.RepaintAll();
            }

            if (Volume.AtlasManager != null && Volume.AtlasManager.IsInitialized)
            {
                if (GUILayout.Button("清除Atlas"))
                {
                    Volume.AtlasManager.ClearAtlases();
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            if (GUILayout.Button("Scene视图聚焦到Volume"))
            {
                FocusSceneViewOnVolume();
            }
        }

        private void FocusSceneViewOnVolume()
        {
            Bounds bounds = Volume.GetWorldBounds();
            SceneView.lastActiveSceneView?.Frame(bounds, false);
        }

        #region Scene GUI

        private void OnSceneGUI()
        {
            if (Volume == null || !Volume.IsInitialized)
                return;

            DrawBoundsHandle();

            DrawVolumeBounds();

            if (m_ShowGridLines)
            {
                DrawGridLines();
            }

            DrawProbes();

            HandleProbeSelection();
        }

        private void DrawBoundsHandle()
        {
            VolumeEditMode editMode = (VolumeEditMode)m_EditModeProperty.enumValueIndex;

            if (editMode != VolumeEditMode.VolumeSizeAutoProbes &&
                editMode != VolumeEditMode.VolumeSizeAutoSpacing)
                return;

            Vector3 volumeSize = m_VolumeSizeProperty.vector3Value;
            Vector3 spacing = m_ProbeSpacingProperty.vector3Value;

            Vector3 localCenter = volumeSize * 0.5f;
            Vector3 worldCenter = Volume.transform.TransformPoint(localCenter);

            m_BoundsHandle.center = Vector3.zero;
            m_BoundsHandle.size = volumeSize;

            Matrix4x4 handleMatrix = Matrix4x4.TRS(
                worldCenter,
                Volume.transform.rotation,
                Vector3.one
            );

            using (new Handles.DrawingScope(handleMatrix))
            {
                EditorGUI.BeginChangeCheck();
                m_BoundsHandle.DrawHandle();

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(Volume.transform, "Edit DDGI Volume Bounds");
                    Undo.RecordObject(target, "Edit DDGI Volume Bounds");

                    Vector3 newSize = m_BoundsHandle.size;
                    Vector3 handleCenter = m_BoundsHandle.center;

                    Vector3 newWorldCenter = Volume.transform.TransformPoint(localCenter) +
                                             Volume.transform.TransformDirection(handleCenter);

                    Vector3 newWorldOrigin = newWorldCenter - Volume.transform.TransformDirection(newSize * 0.5f);

                    Volume.transform.position = newWorldOrigin;

                    m_VolumeSizeProperty.vector3Value = newSize;

                    if (editMode == VolumeEditMode.VolumeSizeAutoProbes)
                    {

                        newSize = Vector3.Max(newSize, spacing);
                        m_VolumeSizeProperty.vector3Value = newSize;

                        Vector3Int probeCounts = new Vector3Int(
                            Mathf.Max(2, Mathf.FloorToInt(newSize.x / spacing.x) + 1),
                            Mathf.Max(2, Mathf.FloorToInt(newSize.y / spacing.y) + 1),
                            Mathf.Max(2, Mathf.FloorToInt(newSize.z / spacing.z) + 1)
                        );
                        m_ProbeCountsProperty.vector3IntValue = probeCounts;
                    }
                    else
                    {

                        Vector3Int probeCounts = m_ProbeCountsProperty.vector3IntValue;
                        probeCounts = new Vector3Int(
                            Mathf.Max(2, probeCounts.x),
                            Mathf.Max(2, probeCounts.y),
                            Mathf.Max(2, probeCounts.z)
                        );

                        Vector3 newSpacing = new Vector3(
                            newSize.x / (probeCounts.x - 1),
                            newSize.y / (probeCounts.y - 1),
                            newSize.z / (probeCounts.z - 1)
                        );
                        m_ProbeSpacingProperty.vector3Value = newSpacing;
                    }

                    serializedObject.ApplyModifiedProperties();
                    Volume.RebuildProbes();
                }
            }

            DrawSizeAnnotations(worldCenter, volumeSize);
        }

        private void DrawSizeAnnotations(Vector3 center, Vector3 size)
        {
            GUIStyle style = new GUIStyle
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = Color.white;

            Transform t = Volume.transform;

            Vector3 xPos = center + t.right * (size.x * 0.5f + 0.5f);
            Handles.Label(xPos, $"{size.x:F1}m", style);

            Vector3 yPos = center + t.up * (size.y * 0.5f + 0.5f);
            Handles.Label(yPos, $"{size.y:F1}m", style);

            Vector3 zPos = center + t.forward * (size.z * 0.5f + 0.5f);
            Handles.Label(zPos, $"{size.z:F1}m", style);
        }

        private void DrawVolumeBounds()
        {
            Bounds bounds = Volume.GetWorldBounds();
            Handles.color = BoundsColor;
            Handles.DrawWireCube(bounds.center, bounds.size);
        }

        private void DrawGridLines()
        {
            var desc = Volume.Descriptor;
            Handles.color = GridLineColor;

            for (int y = 0; y < desc.probeCounts.y; y++)
            {
                for (int z = 0; z < desc.probeCounts.z; z++)
                {
                    Vector3 start = Volume.GetProbeWorldPosition(0, y, z);
                    Vector3 end = Volume.GetProbeWorldPosition(desc.probeCounts.x - 1, y, z);
                    Handles.DrawLine(start, end);
                }
            }

            for (int x = 0; x < desc.probeCounts.x; x++)
            {
                for (int z = 0; z < desc.probeCounts.z; z++)
                {
                    Vector3 start = Volume.GetProbeWorldPosition(x, 0, z);
                    Vector3 end = Volume.GetProbeWorldPosition(x, desc.probeCounts.y - 1, z);
                    Handles.DrawLine(start, end);
                }
            }

            for (int x = 0; x < desc.probeCounts.x; x++)
            {
                for (int y = 0; y < desc.probeCounts.y; y++)
                {
                    Vector3 start = Volume.GetProbeWorldPosition(x, y, 0);
                    Vector3 end = Volume.GetProbeWorldPosition(x, y, desc.probeCounts.z - 1);
                    Handles.DrawLine(start, end);
                }
            }
        }

        private void DrawProbes()
        {
            if (!m_ShowProbes) return;
            var probes = Volume.Probes;

            for (int i = 0; i < probes.Count; i++)
            {
                DDGIProbe probe = probes[i];
                Vector3 pos = probe.ActualPosition;

                Color color;
                if (i == m_SelectedProbeIndex)
                {
                    color = ProbeSelectedColor;
                }
                else
                {
                    color = probe.state switch
                    {
                        ProbeState.Inactive => ProbeInactiveColor,
                        ProbeState.Sleeping => ProbeSleepingColor,
                        _ => ProbeColor
                    };
                }

                float distance = HandleUtility.GetHandleSize(pos);
                float radius = (i == m_SelectedProbeIndex ? SelectedProbeRadius : DefaultProbeRadius)
                               * m_ProbeGizmoSize * distance;

                Handles.color = color;
                Handles.SphereHandleCap(0, pos, Quaternion.identity, radius * 2, EventType.Repaint);

                Handles.color = new Color(color.r, color.g, color.b, 1.0f);
                Handles.DrawWireDisc(pos, Camera.current.transform.forward, radius);

                if (m_ShowProbeIndices)
                {
                    GUIStyle style = new GUIStyle
                    {
                        fontSize = 10,
                        alignment = TextAnchor.MiddleCenter
                    };
                    style.normal.textColor = Color.white;
                    Handles.Label(pos + Vector3.up * radius * 1.5f, probe.flatIndex.ToString(), style);
                }
            }
        }

        private void HandleProbeSelection()
        {
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                float closestDist = float.MaxValue;
                int closestIndex = -1;

                var probes = Volume.Probes;
                for (int i = 0; i < probes.Count; i++)
                {
                    Vector3 pos = probes[i].ActualPosition;
                    float distance = HandleUtility.GetHandleSize(pos);
                    float radius = DefaultProbeRadius * m_ProbeGizmoSize * distance;

                    Vector3 toCenter = pos - ray.origin;
                    float tca = Vector3.Dot(toCenter, ray.direction);
                    float d2 = Vector3.Dot(toCenter, toCenter) - tca * tca;
                    float r2 = radius * radius;

                    if (d2 <= r2)
                    {
                        float thc = Mathf.Sqrt(r2 - d2);
                        float t = tca - thc;

                        if (t > 0 && t < closestDist)
                        {
                            closestDist = t;
                            closestIndex = i;
                        }
                    }
                }

                if (closestIndex >= 0)
                {
                    m_SelectedProbeIndex = closestIndex;
                    e.Use();
                    Repaint();
                    SceneView.RepaintAll();
                }
            }

            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                m_SelectedProbeIndex = -1;
                e.Use();
                Repaint();
                SceneView.RepaintAll();
            }
        }

        #endregion

        #region Static Gizmos

        [DrawGizmo(GizmoType.NotInSelectionHierarchy)]
        static void DrawGizmosNotSelected(DDGIVolume volume, GizmoType gizmoType)
        {
            if (volume == null || !volume.IsInitialized)
                return;

            Bounds bounds = volume.GetWorldBounds();
            Gizmos.color = new Color(BoundsColor.r, BoundsColor.g, BoundsColor.b, 0.3f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            Gizmos.color = new Color(ProbeColor.r, ProbeColor.g, ProbeColor.b, 0.5f);
            foreach (var probe in volume.Probes)
            {
                Gizmos.DrawSphere(probe.ActualPosition, 0.05f);
            }
        }

        #endregion
    }
}
