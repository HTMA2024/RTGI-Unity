using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DDGI
{

    public class DDGIApplyGIRendererFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class Settings
        {
            [Tooltip("GI强度")]
            [Range(0f, 5f)]
            public float giIntensity = 1f;

            [Tooltip("AO强度")]
            [Range(0f, 1f)]
            public float aoIntensity = 1f;

            [Tooltip("渲染注入时机")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        }

        public Settings settings = new Settings();

        private DDGIApplyGIRenderPass m_RenderPass;

        public override void Create()
        {
            m_RenderPass = new DDGIApplyGIRenderPass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {

            if (renderingData.cameraData.cameraType != CameraType.Game &&
                renderingData.cameraData.cameraType != CameraType.SceneView)
                return;

            renderer.EnqueuePass(m_RenderPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_RenderPass?.Dispose();
        }
    }

    public sealed class DDGIApplyGIRenderPass : ScriptableRenderPass, System.IDisposable
    {
        private const string k_ShaderName = "Hidden/DDGI/ApplyGI";
        private const string k_ProfilerTag = "DDGI Apply GI";
        private const string k_AccurateNormalsKeyword = "_DDGI_ACCURATE_NORMALS";

        private readonly DDGIApplyGIRendererFeature.Settings m_Settings;
        private Material m_Material;
        private ProfilingSampler m_ProfilingSampler;

        private static readonly int s_VolumeOrigin = Shader.PropertyToID("_DDGIVolumeOrigin");
        private static readonly int s_VolumeSpacing = Shader.PropertyToID("_DDGIVolumeSpacing");
        private static readonly int s_VolumeProbeCounts = Shader.PropertyToID("_DDGIVolumeProbeCounts");
        private static readonly int s_NormalBias = Shader.PropertyToID("_DDGINormalBias");
        private static readonly int s_ViewBias = Shader.PropertyToID("_DDGIViewBias");
        private static readonly int s_IrradianceGamma = Shader.PropertyToID("_DDGIIrradianceGamma");
        private static readonly int s_IrradianceProbeRes = Shader.PropertyToID("_DDGIIrradianceProbeRes");
        private static readonly int s_DistanceProbeRes = Shader.PropertyToID("_DDGIDistanceProbeRes");
        private static readonly int s_ProbesPerRow = Shader.PropertyToID("_DDGIProbesPerRow");
        private static readonly int s_IrradianceTexelSize = Shader.PropertyToID("_DDGIIrradianceTexelSize");
        private static readonly int s_DistanceTexelSize = Shader.PropertyToID("_DDGIDistanceTexelSize");
        private static readonly int s_GIIntensity = Shader.PropertyToID("_DDGIGIIntensity");
        private static readonly int s_AOIntensity = Shader.PropertyToID("_DDGIAOIntensity");
        private static readonly int s_ProbeDataWidth = Shader.PropertyToID("_DDGIProbeDataWidth");
        private static readonly int s_IrradianceAtlas = Shader.PropertyToID("_DDGIIrradianceAtlas");
        private static readonly int s_DistanceAtlas = Shader.PropertyToID("_DDGIDistanceAtlas");
        private static readonly int s_ProbeData = Shader.PropertyToID("_DDGIProbeData");

        public DDGIApplyGIRenderPass(DDGIApplyGIRendererFeature.Settings settings)
        {
            m_Settings = settings;
            renderPassEvent = settings.renderPassEvent;
            m_ProfilingSampler = new ProfilingSampler(k_ProfilerTag);

            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        private bool EnsureMaterial()
        {
            if (m_Material != null)
                return true;

            Shader shader = Shader.Find(k_ShaderName);
            if (shader == null)
            {
                Debug.LogError("[DDGIApplyGI] Cannot find shader: " + k_ShaderName);
                return false;
            }

            m_Material = CoreUtils.CreateEngineMaterial(shader);
            return m_Material != null;
        }

        private bool FindDDGIResources(out DDGIVolume volume, out DDGIRaytracingManager rtManager)
        {
            volume = null;
            rtManager = null;

            DDGIProbeUpdater updater = Object.FindObjectOfType<DDGIProbeUpdater>();
            if (updater == null || !updater.IsInitialized || !updater.IsUsingRaytracing)
                return false;

            rtManager = updater.RaytracingManager;
            if (rtManager == null || !rtManager.IsInitialized)
                return false;

            volume = updater.GetComponent<DDGIVolume>();
            if (volume == null || !volume.IsInitialized)
                return false;

            if (volume.AtlasManager == null || !volume.AtlasManager.IsInitialized)
                return false;

            return true;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!EnsureMaterial())
                return;

            if (!FindDDGIResources(out DDGIVolume volume, out DDGIRaytracingManager rtManager))
                return;

            CommandBuffer cmd = CommandBufferPool.Get(k_ProfilerTag);

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                SetShaderParameters(cmd, volume, rtManager);

                bool useAccurateNormals = false;
                var ssgiVolume = VolumeManager.instance.stack.GetComponent<URPSSGI.SSGIVolumeComponent>();
                if (ssgiVolume != null)
                    useAccurateNormals = ssgiVolume.useAccurateNormals.value;
                CoreUtils.SetKeyword(m_Material, k_AccurateNormalsKeyword, useAccurateNormals);

                cmd.DrawProcedural(Matrix4x4.identity, m_Material, 0, MeshTopology.Triangles, 3);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private void SetShaderParameters(CommandBuffer cmd, DDGIVolume volume, DDGIRaytracingManager rtManager)
        {
            var desc = volume.Descriptor;
            var atlasManager = volume.AtlasManager;
            var atlasConfig = volume.AtlasConfig;

            Vector3 volumeOrigin = volume.transform.position;
            cmd.SetGlobalVector(s_VolumeOrigin,
                new Vector4(volumeOrigin.x, volumeOrigin.y, volumeOrigin.z, 0));
            cmd.SetGlobalVector(s_VolumeSpacing,
                new Vector4(desc.probeSpacing.x, desc.probeSpacing.y, desc.probeSpacing.z, 0));
            cmd.SetGlobalVector(s_VolumeProbeCounts,
                new Vector4(desc.probeCounts.x, desc.probeCounts.y, desc.probeCounts.z, desc.TotalProbeCount));

            cmd.SetGlobalFloat(s_NormalBias, rtManager.NormalBias);
            cmd.SetGlobalFloat(s_ViewBias, desc.viewBias);
            cmd.SetGlobalFloat(s_IrradianceGamma, desc.irradianceGamma);
            cmd.SetGlobalFloat(s_GIIntensity, m_Settings.giIntensity);
            cmd.SetGlobalFloat(s_AOIntensity, m_Settings.aoIntensity);

            cmd.SetGlobalFloat(s_IrradianceProbeRes, atlasConfig.irradianceProbeResolution);
            cmd.SetGlobalFloat(s_DistanceProbeRes, atlasConfig.distanceProbeResolution);
            cmd.SetGlobalFloat(s_ProbesPerRow, atlasManager.ProbesPerRow);

            var irrSize = atlasManager.IrradianceAtlasSize;
            var distSize = atlasManager.DistanceAtlasSize;
            cmd.SetGlobalVector(s_IrradianceTexelSize,
                new Vector4(1f / irrSize.x, 1f / irrSize.y, 0, 0));
            cmd.SetGlobalVector(s_DistanceTexelSize,
                new Vector4(1f / distSize.x, 1f / distSize.y, 0, 0));

            cmd.SetGlobalTexture(s_IrradianceAtlas, atlasManager.IrradianceAtlas);
            cmd.SetGlobalTexture(s_DistanceAtlas, atlasManager.DistanceAtlas);

            RenderTexture probeDataTex = rtManager.ProbeDataTexture;
            if (probeDataTex != null && desc.enableProbeClassification)
            {
                cmd.SetGlobalTexture(s_ProbeData, probeDataTex);

                int probeDataWidth = Mathf.CeilToInt(Mathf.Sqrt(desc.TotalProbeCount));
                cmd.SetGlobalFloat(s_ProbeDataWidth, probeDataWidth);
            }
            else
            {
                cmd.SetGlobalFloat(s_ProbeDataWidth, 0);
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }
}
