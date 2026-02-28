using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDGI
{

    public enum ProbeVisualizationMode
    {

        Irradiance = 0,

        Distance = 1,

        Normal = 2,

        State = 3,

        HitPosition = 10,

        HitNormal = 11,

        HitAlbedo = 12,

        HitEmission = 13,

        HitDistance = 14,

        DirectIrradiance = 15,

        IndirectIrradiance = 16,

        OutgoingRadiance = 17,

        ProbeOffset = 20,

        ProbeRelocationState = 21,

        ProbeClassificationState = 22,

        ProbeVariability = 30
    }

    [RequireComponent(typeof(DDGIVolume))]
    [ExecuteInEditMode]
    public class DDGIProbeVisualizer : MonoBehaviour
    {
        #region Serialized Fields

        [Header("可视化设置")]
        [SerializeField]
        private bool m_EnableVisualization = true;

        [SerializeField]
        private ProbeVisualizationMode m_VisualizationMode = ProbeVisualizationMode.State;

        [SerializeField]
        [Range(0.01f, 1.0f)]
        private float m_ProbeRadius = 0.15f;

        [SerializeField]
        [Range(0.1f, 5.0f)]
        private float m_Intensity = 1.0f;

        [SerializeField]
        private Mesh m_SphereMesh;

        [SerializeField]
        private Material m_VisualizationMaterial;

        [Header("状态颜色")]
        [SerializeField]
        [Tooltip("Active状态颜色，alpha=1表示完全显示Atlas数据，alpha=0显示纯状态色")]
        private Color m_ActiveColor = new Color(0.3f, 0.7f, 1.0f, 1.0f);

        [SerializeField]
        [Tooltip("Inactive状态颜色，alpha=1表示完全显示Atlas数据，alpha=0显示纯状态色")]
        private Color m_InactiveColor = new Color(0.5f, 0.5f, 0.5f, 0.0f);

        [SerializeField]
        private Color m_SleepingColor = new Color(1.0f, 0.8f, 0.2f, 0.5f);

        #endregion

        #region Private Fields

        private DDGIVolume m_Volume;
        private DDGIRaytracingManager m_RaytracingManager;
        private MaterialPropertyBlock m_PropertyBlock;

        private Matrix4x4[] m_Matrices;
        private Vector4[] m_ProbePositions;
        private Vector4[] m_ProbeAtlasUVs;
        private Vector4[] m_ProbeColors;

        private static readonly int PropIrradianceAtlas = Shader.PropertyToID("_IrradianceAtlas");
        private static readonly int PropDistanceAtlas = Shader.PropertyToID("_DistanceAtlas");
        private static readonly int PropIrradianceAtlasParams = Shader.PropertyToID("_IrradianceAtlasParams");
        private static readonly int PropDistanceAtlasParams = Shader.PropertyToID("_DistanceAtlasParams");
        private static readonly int PropProbeRadius = Shader.PropertyToID("_ProbeRadius");
        private static readonly int PropIntensity = Shader.PropertyToID("_Intensity");
        private static readonly int PropVisualizationMode = Shader.PropertyToID("_VisualizationMode");
        private static readonly int PropProbePosition = Shader.PropertyToID("_ProbePosition");
        private static readonly int PropProbeAtlasUV = Shader.PropertyToID("_ProbeAtlasUV");
        private static readonly int PropProbeColor = Shader.PropertyToID("_ProbeColor");

        private static readonly int PropGBufferPositionDistance = Shader.PropertyToID("_GBuffer_PositionDistance");
        private static readonly int PropGBufferNormalHitFlag = Shader.PropertyToID("_GBuffer_NormalHitFlag");
        private static readonly int PropGBufferAlbedoRoughness = Shader.PropertyToID("_GBuffer_AlbedoRoughness");
        private static readonly int PropGBufferEmissionMetallic = Shader.PropertyToID("_GBuffer_EmissionMetallic");
        private static readonly int PropGBufferWidth = Shader.PropertyToID("_GBufferWidth");
        private static readonly int PropGBufferHeight = Shader.PropertyToID("_GBufferHeight");
        private static readonly int PropRaysPerProbe = Shader.PropertyToID("_RaysPerProbe");
        private static readonly int PropFixedRayCount = Shader.PropertyToID("_FixedRayCount");

        private static readonly int PropDirectIrradianceBuffer = Shader.PropertyToID("_DirectIrradianceBuffer");
        private static readonly int PropIndirectIrradianceBuffer = Shader.PropertyToID("_IndirectIrradianceBuffer");
        private static readonly int PropRadianceBuffer = Shader.PropertyToID("_RadianceBuffer");

        private static readonly int PropRayRotationMatrix = Shader.PropertyToID("_RayRotationMatrix");

        private static readonly int PropIrradianceGamma = Shader.PropertyToID("_IrradianceGamma");

        private static readonly int PropProbeData = Shader.PropertyToID("_ProbeData");
        private static readonly int PropProbeDataWidth = Shader.PropertyToID("_ProbeDataWidth");
        private static readonly int PropProbeSpacing = Shader.PropertyToID("_ProbeSpacing");
        private static readonly int PropBackfaceThreshold = Shader.PropertyToID("_BackfaceThreshold");
        private static readonly int PropMinFrontfaceDistance = Shader.PropertyToID("_MinFrontfaceDistance");

        private static readonly int PropProbeVariability = Shader.PropertyToID("_ProbeVariability");

        private const int MaxInstancesPerBatch = 1023;

        #endregion

        #region Properties

        public bool EnableVisualization
        {
            get => m_EnableVisualization;
            set => m_EnableVisualization = value;
        }

        public ProbeVisualizationMode VisualizationMode
        {
            get => m_VisualizationMode;
            set => m_VisualizationMode = value;
        }

        public float ProbeRadius
        {
            get => m_ProbeRadius;
            set => m_ProbeRadius = Mathf.Clamp(value, 0.01f, 1.0f);
        }

        public float Intensity
        {
            get => m_Intensity;
            set => m_Intensity = Mathf.Clamp(value, 0.1f, 5.0f);
        }

        public bool RequiresRaytracingData
        {
            get => (int)m_VisualizationMode >= 10;
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            m_Volume = GetComponent<DDGIVolume>();
            m_PropertyBlock = new MaterialPropertyBlock();

            EnsureResources();
        }

        private void OnDisable()
        {
            m_Matrices = null;
            m_ProbePositions = null;
            m_ProbeAtlasUVs = null;
            m_ProbeColors = null;
        }

        private void Update()
        {
            if (!m_EnableVisualization || m_Volume == null || !m_Volume.IsInitialized)
                return;

            RenderProbes();
        }

        #endregion

        #region Public Methods

        public void SetRaytracingManager(DDGIRaytracingManager manager)
        {
            m_RaytracingManager = manager;
        }

        public void RefreshVisualization()
        {
            if (m_Volume == null || !m_Volume.IsInitialized)
                return;

            UpdateInstanceData();
        }

        #endregion

        #region Private Methods

        private void EnsureResources()
        {

            if (m_SphereMesh == null)
            {

            }

            if (m_VisualizationMaterial == null)
            {
                Shader shader = Shader.Find("DDGI/ProbeVisualization");
                if (shader != null)
                {
                    m_VisualizationMaterial = new Material(shader);
                    m_VisualizationMaterial.name = "DDGI_ProbeVisualization";
                    m_VisualizationMaterial.enableInstancing = true;
                }
                else
                {
                    Debug.LogWarning("[DDGIProbeVisualizer] Could not find DDGI/ProbeVisualization shader");
                }
            }
        }

        private void UpdateInstanceData()
        {
            var probes = m_Volume.Probes;
            int probeCount = probes.Count;

            if (probeCount == 0)
                return;

            if (m_Matrices == null || m_Matrices.Length != probeCount)
            {
                m_Matrices = new Matrix4x4[probeCount];
                m_ProbePositions = new Vector4[probeCount];
                m_ProbeAtlasUVs = new Vector4[probeCount];
                m_ProbeColors = new Vector4[probeCount];
            }

            for (int i = 0; i < probeCount; i++)
            {
                DDGIProbe probe = probes[i];
                Vector3 pos = probe.ActualPosition;

                m_Matrices[i] = Matrix4x4.TRS(pos, Quaternion.identity, Vector3.one);

                m_ProbePositions[i] = new Vector4(pos.x, pos.y, pos.z, probe.flatIndex);

                m_ProbeAtlasUVs[i] = new Vector4(
                    probe.irradianceAtlasUV.x,
                    probe.irradianceAtlasUV.y,
                    probe.distanceAtlasUV.x,
                    probe.distanceAtlasUV.y
                );

                Color stateColor;
                switch (probe.state)
                {
                    case ProbeState.Inactive:
                        stateColor = m_InactiveColor;
                        break;
                    case ProbeState.Sleeping:
                        stateColor = m_SleepingColor;
                        break;
                    default:
                        stateColor = m_ActiveColor;
                        break;
                }
                m_ProbeColors[i] = stateColor;
            }
        }

        private void RenderProbes()
        {
            if (m_SphereMesh == null || m_VisualizationMaterial == null)
            {
                EnsureResources();
                if (m_SphereMesh == null || m_VisualizationMaterial == null)
                    return;
            }

            UpdateInstanceData();

            var probes = m_Volume.Probes;
            int probeCount = probes.Count;

            if (probeCount == 0)
                return;

            m_VisualizationMaterial.SetFloat(PropProbeRadius, m_ProbeRadius);
            m_VisualizationMaterial.SetFloat(PropIntensity, m_Intensity);
            m_VisualizationMaterial.SetInt(PropVisualizationMode, (int)m_VisualizationMode);

            if (m_Volume.AtlasManager != null && m_Volume.AtlasManager.IsInitialized)
            {
                m_VisualizationMaterial.SetTexture(PropIrradianceAtlas, m_Volume.AtlasManager.IrradianceAtlas);
                m_VisualizationMaterial.SetTexture(PropDistanceAtlas, m_Volume.AtlasManager.DistanceAtlas);
                m_VisualizationMaterial.SetVector(PropIrradianceAtlasParams, m_Volume.AtlasManager.GetIrradianceAtlasParams());
                m_VisualizationMaterial.SetVector(PropDistanceAtlasParams, m_Volume.AtlasManager.GetDistanceAtlasParams());

                m_VisualizationMaterial.SetFloat(PropIrradianceGamma, m_Volume.Descriptor.irradianceGamma);
            }

            if (RequiresRaytracingData && m_RaytracingManager != null && m_RaytracingManager.IsInitialized)
            {
                m_VisualizationMaterial.SetTexture(PropGBufferPositionDistance, m_RaytracingManager.GBufferPositionDistance);
                m_VisualizationMaterial.SetTexture(PropGBufferNormalHitFlag, m_RaytracingManager.GBufferNormalHitFlag);
                m_VisualizationMaterial.SetTexture(PropGBufferAlbedoRoughness, m_RaytracingManager.GBufferAlbedoRoughness);
                m_VisualizationMaterial.SetTexture(PropGBufferEmissionMetallic, m_RaytracingManager.GBufferEmissionMetallic);

                m_VisualizationMaterial.SetTexture(PropDirectIrradianceBuffer, m_RaytracingManager.DirectIrradianceBuffer);
                m_VisualizationMaterial.SetTexture(PropIndirectIrradianceBuffer, m_RaytracingManager.IndirectIrradianceBuffer);
                m_VisualizationMaterial.SetTexture(PropRadianceBuffer, m_RaytracingManager.RadianceBuffer);

                Vector2Int gBufferSize = m_RaytracingManager.GetGBufferSize();
                m_VisualizationMaterial.SetInt(PropGBufferWidth, gBufferSize.x);
                m_VisualizationMaterial.SetInt(PropGBufferHeight, gBufferSize.y);
                m_VisualizationMaterial.SetInt(PropRaysPerProbe, m_RaytracingManager.RaysPerProbe);
                m_VisualizationMaterial.SetInt(PropFixedRayCount, m_RaytracingManager.FixedRayCount);

                m_VisualizationMaterial.SetMatrix(PropRayRotationMatrix, m_RaytracingManager.GetCurrentRayRotationMatrix());
            }

            if (m_RaytracingManager != null && m_RaytracingManager.IsInitialized && m_RaytracingManager.ProbeDataTexture != null)
            {
                m_VisualizationMaterial.SetTexture(PropProbeData, m_RaytracingManager.ProbeDataTexture);
                m_VisualizationMaterial.SetInt(PropProbeDataWidth, m_RaytracingManager.ProbeDataTexture.width);
                m_VisualizationMaterial.SetVector(PropProbeSpacing, m_Volume.Descriptor.probeSpacing);
                m_VisualizationMaterial.SetFloat(PropBackfaceThreshold, m_Volume.Descriptor.probeBackfaceThreshold);
                m_VisualizationMaterial.SetFloat(PropMinFrontfaceDistance, m_Volume.Descriptor.probeMinFrontfaceDistance);
            }

            if (m_RaytracingManager != null && m_RaytracingManager.IsInitialized && m_RaytracingManager.ProbeVariabilityTexture != null)
            {
                m_VisualizationMaterial.SetTexture(PropProbeVariability, m_RaytracingManager.ProbeVariabilityTexture);
            }

            for (int batchStart = 0; batchStart < probeCount; batchStart += MaxInstancesPerBatch)
            {
                int batchCount = Mathf.Min(MaxInstancesPerBatch, probeCount - batchStart);

                m_PropertyBlock.Clear();
                m_PropertyBlock.SetVectorArray(PropProbePosition, GetSubArray(m_ProbePositions, batchStart, batchCount));
                m_PropertyBlock.SetVectorArray(PropProbeAtlasUV, GetSubArray(m_ProbeAtlasUVs, batchStart, batchCount));
                m_PropertyBlock.SetVectorArray(PropProbeColor, GetSubArray(m_ProbeColors, batchStart, batchCount));

                Graphics.DrawMeshInstanced(
                    m_SphereMesh,
                    0,
                    m_VisualizationMaterial,
                    GetSubArray(m_Matrices, batchStart, batchCount),
                    batchCount,
                    m_PropertyBlock,
                    ShadowCastingMode.Off,
                    false,
                    gameObject.layer
                );
            }
        }

        private T[] GetSubArray<T>(T[] source, int start, int count)
        {
            if (start == 0 && count == source.Length)
                return source;

            T[] result = new T[count];
            System.Array.Copy(source, start, result, 0, count);
            return result;
        }

        private Mesh CreateSphereMesh(int longitudeSegments, int latitudeSegments)
        {
            Mesh mesh = new Mesh();
            mesh.name = "DDGI_ProbeSphere";

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int lat = 0; lat <= latitudeSegments; lat++)
            {
                float theta = lat * Mathf.PI / latitudeSegments;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= longitudeSegments; lon++)
                {
                    float phi = lon * 2 * Mathf.PI / longitudeSegments;
                    float sinPhi = Mathf.Sin(phi);
                    float cosPhi = Mathf.Cos(phi);

                    Vector3 normal = new Vector3(
                        cosPhi * sinTheta,
                        cosTheta,
                        sinPhi * sinTheta
                    );

                    vertices.Add(normal);
                    normals.Add(normal);
                }
            }

            for (int lat = 0; lat < latitudeSegments; lat++)
            {
                for (int lon = 0; lon < longitudeSegments; lon++)
                {
                    int current = lat * (longitudeSegments + 1) + lon;
                    int next = current + longitudeSegments + 1;

                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);

                    triangles.Add(current + 1);
                    triangles.Add(next);
                    triangles.Add(next + 1);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            return mesh;
        }

        #endregion

        #region Editor Support

#if UNITY_EDITOR
        private void OnValidate()
        {
            m_ProbeRadius = Mathf.Clamp(m_ProbeRadius, 0.01f, 1.0f);
            m_Intensity = Mathf.Clamp(m_Intensity, 0.1f, 5.0f);
        }
#endif

        #endregion
    }
}
