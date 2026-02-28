using UnityEngine;
using UnityEditor;

namespace DDGI
{
    [CustomEditor(typeof(DDGISkyOnlyValidator))]
    public sealed class DDGISkyOnlyValidatorEditor : Editor
    {
        private Vector2 m_ScrollPos;
        private bool m_ShowStep0;
        private bool m_ShowStep1;
        private bool m_ShowStep2;
        private bool m_ShowStep3;
        private bool m_ShowStep4;
        private bool m_ShowFullLog;
        private bool m_ShowGTVis = true;

        private static readonly string[] s_GTDisplayModeNames =
        {
            "GT (Ground Truth)",
            "Unity SH",
            "Error (|GT-SH|x5)",
            "Side-by-side (GT|SH)"
        };

        public override void OnInspectorGUI()
        {
            var validator = (DDGISkyOnlyValidator)target;

            EditorGUILayout.HelpBox(
                "DDGI Sky-Only Ground Truth 验证器\n" +
                "在纯天空球场景（无几何体）下逐阶段对比GPU计算与CPU Ground Truth。\n\n" +
                "使用前请确保：\n" +
                "1. 场景中关闭所有MeshRenderer\n" +
                "2. 天空盒设置正确且使用Cubemap材质\n" +
                "3. Unity LightProbe已烘焙\n" +
                "4. DDGI已初始化并运行了至少一帧",
                MessageType.Info);

            EditorGUILayout.Space(5);

            DrawDefaultInspector();

            EditorGUILayout.Space(10);

            m_ShowGTVis = EditorGUILayout.Foldout(m_ShowGTVis, "GT Probe 可视化", true, EditorStyles.foldoutHeader);
            if (m_ShowGTVis)
            {
                EditorGUI.indentLevel++;
                DrawGTVisualizationGUI(validator);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField("验证操作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            {
                GUI.backgroundColor = new Color(0.4f, 0.8f, 1f);
                if (GUILayout.Button("执行完整验证", GUILayout.Height(30)))
                {
                    validator.RunFullValidation();
                }

                GUI.backgroundColor = new Color(1f, 0.8f, 0.3f);
                if (GUILayout.Button("单帧精确验证\n(Hysteresis=0)", GUILayout.Height(30)))
                {
                    if (EditorUtility.DisplayDialog("单帧精确验证",
                        "此操作将：\n" +
                        "1. 临时将Hysteresis设为0\n" +
                        "2. 清除Atlas\n" +
                        "3. 强制执行一帧更新\n" +
                        "4. 执行验证\n" +
                        "5. 恢复Hysteresis原始值\n\n" +
                        "继续？", "执行", "取消"))
                    {
                        validator.RunSingleFrameValidation();
                    }
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField("单步验证", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button("Step 0\nGT vs Unity"))
                {
                    var r = validator.ValidateStep0_GTvsUnityLightProbe();
                    Debug.Log($"[Step 0] {(r.passed ? "PASS" : "FAIL")} RMSE={r.rmse:F6}\n{r.details}");
                }
                if (GUILayout.Button("Step 1\nRayGen"))
                {
                    var r = validator.ValidateStep1_RayGen();
                    Debug.Log($"[Step 1] {(r.passed ? "PASS" : "FAIL")} RMSE={r.rmse:F6}\n{r.details}");
                }
                if (GUILayout.Button("Step 2\nLighting"))
                {
                    var r = validator.ValidateStep2_Lighting();
                    Debug.Log($"[Step 2] {(r.passed ? "PASS" : "FAIL")} RMSE={r.rmse:F6}\n{r.details}");
                }
                if (GUILayout.Button("Step 3\nMonteCarlo"))
                {
                    var r = validator.ValidateStep3_MonteCarlo();
                    Debug.Log($"[Step 3] {(r.passed ? "PASS" : "FAIL")} RMSE={r.rmse:F6}\n{r.details}");
                }
                if (GUILayout.Button("Step 4\nDDGI vs Unity"))
                {
                    var r = validator.ValidateStep4_DDGIvsUnity();
                    Debug.Log($"[Step 4] {(r.passed ? "PASS" : "FAIL")} RMSE={r.rmse:F6}\n{r.details}");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            var report = validator.lastReport;
            if (report != null)
            {
                EditorGUILayout.LabelField("上次验证结果", EditorStyles.boldLabel);
                EditorGUILayout.Space(3);

                DrawPhaseResultSummary("Step 0: GT vs Unity LightProbe", report.step0_GTvsUnity);
                DrawPhaseResultSummary("Step 1: Phase 1 - RayGen", report.step1_RayGen);
                DrawPhaseResultSummary("Step 2: Phase 2 - Lighting", report.step2_Lighting);
                DrawPhaseResultSummary("Step 3: Phase 3 - MonteCarlo", report.step3_MonteCarlo);
                DrawPhaseResultSummary("Step 4: DDGI vs Unity", report.step4_Final);

                EditorGUILayout.Space(5);

                DrawPhaseDetails("Step 0 详细", report.step0_GTvsUnity, ref m_ShowStep0);
                DrawPhaseDetails("Step 1 详细", report.step1_RayGen, ref m_ShowStep1);
                DrawPhaseDetails("Step 2 详细", report.step2_Lighting, ref m_ShowStep2);
                DrawPhaseDetails("Step 3 详细", report.step3_MonteCarlo, ref m_ShowStep3);
                DrawPhaseDetails("Step 4 详细", report.step4_Final, ref m_ShowStep4);

                EditorGUILayout.Space(5);

                m_ShowFullLog = EditorGUILayout.Foldout(m_ShowFullLog, "完整日志");
                if (m_ShowFullLog && !string.IsNullOrEmpty(report.fullLog))
                {
                    m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos, GUILayout.MaxHeight(400));
                    EditorGUILayout.TextArea(report.fullLog, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.EndScrollView();

                    if (GUILayout.Button("复制到剪贴板"))
                    {
                        GUIUtility.systemCopyBuffer = report.fullLog;
                        Debug.Log("验证报告已复制到剪贴板");
                    }
                }
            }
        }

        private void DrawGTVisualizationGUI(DDGISkyOnlyValidator validator)
        {

            EditorGUILayout.LabelField("烘焙参数", EditorStyles.miniLabel);
            validator.gtTextureResolution = EditorGUILayout.IntPopup("贴图分辨率",
                validator.gtTextureResolution,
                new[] { "16", "32", "64", "128" },
                new[] { 16, 32, 64, 128 });
            validator.gtMCSamples = EditorGUILayout.IntPopup("MC采样数",
                validator.gtMCSamples,
                new[] { "256", "1024", "4096", "8192", "16384", "32768" },
                new[] { 256, 1024, 4096, 8192, 16384, 32768 });

            EditorGUILayout.Space(3);

            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.5f);
            if (GUILayout.Button("烘焙 GT 贴图", GUILayout.Height(25)))
            {
                EditorUtility.DisplayProgressBar("烘焙GT贴图", "正在计算蒙特卡洛积分...", 0f);
                try
                {
                    validator.BakeGTTexture();
                    validator.gtVisualizationEnabled = true;
                    SceneView.RepaintAll();
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                }
            }
            GUI.backgroundColor = Color.white;

            if (validator.gtTexture != null)
            {
                EditorGUILayout.Space(5);

                EditorGUILayout.LabelField($"GT贴图: {validator.gtTexture.width}x{validator.gtTexture.height}",
                    EditorStyles.miniLabel);
                Rect previewRect = GUILayoutUtility.GetRect(64, 64, GUILayout.ExpandWidth(false));
                EditorGUI.DrawPreviewTexture(previewRect, validator.gtTexture);

                EditorGUILayout.Space(3);

                EditorGUI.BeginChangeCheck();

                bool newEnabled = EditorGUILayout.Toggle("显示GT球体", validator.gtVisualizationEnabled);
                int newDisplayMode = EditorGUILayout.Popup("显示模式", validator.gtDisplayMode, s_GTDisplayModeNames);
                float newRadius = EditorGUILayout.Slider("球体半径", validator.gtProbeRadius, 0.05f, 2.0f);
                float newIntensity = EditorGUILayout.Slider("亮度", validator.gtIntensity, 0.1f, 10.0f);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(validator, "修改GT可视化参数");
                    validator.gtVisualizationEnabled = newEnabled;
                    validator.gtDisplayMode = newDisplayMode;
                    validator.gtProbeRadius = newRadius;
                    validator.gtIntensity = newIntensity;
                    SceneView.RepaintAll();
                }

                EditorGUILayout.Space(3);

                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button("清除GT可视化"))
                {
                    validator.CleanupGTVisualization();
                    SceneView.RepaintAll();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        private void DrawPhaseResultSummary(string label, DDGISkyOnlyValidator.PhaseResult phase)
        {
            if (phase == null)
            {
                EditorGUILayout.LabelField(label, "未执行");
                return;
            }

            Color bgColor = phase.passed ? new Color(0.2f, 0.7f, 0.2f) : new Color(0.9f, 0.2f, 0.2f);
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                string status = phase.passed ? "PASS" : "FAIL";
                EditorGUILayout.LabelField($"{status}", EditorStyles.boldLabel, GUILayout.Width(40));
                EditorGUILayout.LabelField(label, GUILayout.MinWidth(200));
                EditorGUILayout.LabelField($"RMSE={phase.rmse:F6}  MaxErr={phase.maxError:F6}", GUILayout.MinWidth(200));
            }
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = prevBg;
        }

        private void DrawPhaseDetails(string label, DDGISkyOnlyValidator.PhaseResult phase, ref bool foldout)
        {
            if (phase == null) return;

            foldout = EditorGUILayout.Foldout(foldout, label);
            if (foldout && !string.IsNullOrEmpty(phase.details))
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.TextArea(phase.details, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel--;
            }
        }
    }
}
