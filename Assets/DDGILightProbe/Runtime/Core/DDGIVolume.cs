using System.Collections.Generic;
using UnityEngine;

namespace DDGI
{

    [ExecuteInEditMode]
    public class DDGIVolume : MonoBehaviour
    {
        [SerializeField]
        private DDGIVolumeDescriptor m_Descriptor = DDGIVolumeDescriptor.Default;

        [SerializeField]
        private DDGIAtlasConfig m_AtlasConfig = DDGIAtlasConfig.Default;

        private List<DDGIProbe> m_Probes = new List<DDGIProbe>();

        private DDGIAtlasManager m_AtlasManager;

        private bool m_Initialized = false;

        #region Properties

        public DDGIVolumeDescriptor Descriptor
        {
            get => m_Descriptor;
            set
            {
                m_Descriptor = value;
                RebuildProbes();
            }
        }

        public IReadOnlyList<DDGIProbe> Probes => m_Probes;

        public int ProbeCount => m_Probes.Count;

        public bool IsInitialized => m_Initialized;

        public DDGIAtlasManager AtlasManager => m_AtlasManager;

        public DDGIAtlasConfig AtlasConfig
        {
            get => m_AtlasConfig;
            set
            {
                m_AtlasConfig = value;
                RebuildAtlas();
            }
        }

        #endregion

        #region Unity Lifecycle

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            Cleanup();
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        private void OnValidate()
        {

            m_Descriptor.probeCounts = Vector3Int.Max(m_Descriptor.probeCounts, new Vector3Int(2, 2, 2));

            m_Descriptor.probeSpacing = Vector3.Max(m_Descriptor.probeSpacing, Vector3.one * 0.1f);

            if (m_Initialized)
            {
                RebuildProbes();
            }
        }

        #endregion

        #region Public Methods

        public void Initialize()
        {
            if (m_Initialized)
                return;

            RebuildProbes();
            m_Initialized = true;
        }

        public void Cleanup()
        {
            m_Probes.Clear();

            if (m_AtlasManager != null)
            {
                m_AtlasManager.Dispose();
                m_AtlasManager = null;
            }

            m_Initialized = false;
        }

        public void RebuildProbes()
        {
            m_Probes.Clear();

            int totalCount = m_Descriptor.TotalProbeCount;
            m_Probes.Capacity = totalCount;

            for (int z = 0; z < m_Descriptor.probeCounts.z; z++)
            {
                for (int y = 0; y < m_Descriptor.probeCounts.y; y++)
                {
                    for (int x = 0; x < m_Descriptor.probeCounts.x; x++)
                    {
                        Vector3Int gridIndex = new Vector3Int(x, y, z);
                        int flatIndex = m_Descriptor.GetProbeIndex(x, y, z);

                        Vector3 localPos = m_Descriptor.GetProbeLocalPosition(x, y, z);
                        Vector3 worldPos = transform.TransformPoint(localPos);

                        DDGIProbe probe = new DDGIProbe(gridIndex, flatIndex, worldPos);
                        m_Probes.Add(probe);
                    }
                }
            }

            RebuildAtlas();
        }

        public void RebuildAtlas()
        {
            if (m_Probes.Count == 0)
                return;

            if (m_AtlasManager == null)
            {
                m_AtlasManager = new DDGIAtlasManager(m_AtlasConfig);
            }

            m_AtlasManager.Initialize(m_Probes.Count);
            m_AtlasManager.UpdateProbeAtlasUVs(m_Probes);
        }

        public DDGIProbe GetProbe(int x, int y, int z)
        {
            if (!m_Descriptor.IsValidIndex(x, y, z))
                return null;

            int flatIndex = m_Descriptor.GetProbeIndex(x, y, z);
            return m_Probes[flatIndex];
        }

        public DDGIProbe GetProbe(Vector3Int index)
        {
            return GetProbe(index.x, index.y, index.z);
        }

        public DDGIProbe GetProbe(int flatIndex)
        {
            if (flatIndex < 0 || flatIndex >= m_Probes.Count)
                return null;

            return m_Probes[flatIndex];
        }

        public Vector3 GetProbeWorldPosition(int x, int y, int z)
        {
            Vector3 localPos = m_Descriptor.GetProbeLocalPosition(x, y, z);
            return transform.TransformPoint(localPos);
        }

        public Vector3 GetProbeWorldPosition(int flatIndex)
        {
            Vector3Int index3D = m_Descriptor.GetProbeIndex3D(flatIndex);
            return GetProbeWorldPosition(index3D.x, index3D.y, index3D.z);
        }

        public Bounds GetWorldBounds()
        {
            Bounds localBounds = m_Descriptor.GetLocalBounds();

            Vector3 worldCenter = transform.TransformPoint(localBounds.center);
            Vector3 worldSize = Vector3.Scale(localBounds.size, transform.lossyScale);

            return new Bounds(worldCenter, worldSize);
        }

        public bool GetProbeCage(Vector3 worldPos, out Vector3Int baseIndex, out Vector3 alpha)
        {

            Vector3 localPos = transform.InverseTransformPoint(worldPos);

            Vector3 gridPos = new Vector3(
                localPos.x / m_Descriptor.probeSpacing.x,
                localPos.y / m_Descriptor.probeSpacing.y,
                localPos.z / m_Descriptor.probeSpacing.z
            );

            baseIndex = new Vector3Int(
                Mathf.FloorToInt(gridPos.x),
                Mathf.FloorToInt(gridPos.y),
                Mathf.FloorToInt(gridPos.z)
            );

            baseIndex = Vector3Int.Max(baseIndex, Vector3Int.zero);
            baseIndex = Vector3Int.Min(baseIndex, m_Descriptor.probeCounts - new Vector3Int(2, 2, 2));

            alpha = new Vector3(
                gridPos.x - baseIndex.x,
                gridPos.y - baseIndex.y,
                gridPos.z - baseIndex.z
            );
            alpha = Vector3.Max(Vector3.zero, Vector3.Min(Vector3.one, alpha));

            return gridPos.x >= 0 && gridPos.x <= m_Descriptor.probeCounts.x - 1 &&
                   gridPos.y >= 0 && gridPos.y <= m_Descriptor.probeCounts.y - 1 &&
                   gridPos.z >= 0 && gridPos.z <= m_Descriptor.probeCounts.z - 1;
        }

        public void UpdateProbePositions()
        {
            for (int i = 0; i < m_Probes.Count; i++)
            {
                DDGIProbe probe = m_Probes[i];
                Vector3 localPos = m_Descriptor.GetProbeLocalPosition(probe.gridIndex);
                probe.position = transform.TransformPoint(localPos);
            }
        }

        #endregion

        #region Editor Support

#if UNITY_EDITOR

        private void OnDrawGizmosSelected()
        {

            Bounds bounds = GetWorldBounds();
            Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
#endif

        #endregion
    }
}
