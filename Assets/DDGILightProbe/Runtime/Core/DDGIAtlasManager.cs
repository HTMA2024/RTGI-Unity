using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace DDGI
{

    [Serializable]
    public struct DDGIAtlasConfig
    {

        public int irradianceProbeResolution;

        public int distanceProbeResolution;

        public int gutterSize;

        public static DDGIAtlasConfig Default => new DDGIAtlasConfig
        {
            irradianceProbeResolution = 8,
            distanceProbeResolution = 16,
            gutterSize = 1
        };

        public int IrradianceProbeSizeWithGutter => irradianceProbeResolution + gutterSize * 2;

        public int DistanceProbeSizeWithGutter => distanceProbeResolution + gutterSize * 2;
    }

    public class DDGIAtlasManager : IDisposable
    {
        #region Constants

        private const RenderTextureFormat IrradianceFormat = RenderTextureFormat.ARGB2101010;
        private const RenderTextureFormat DistanceFormat = RenderTextureFormat.RGHalf;

        #endregion

        #region Fields

        private DDGIAtlasConfig m_Config;
        private int m_ProbeCount;

        private int m_IrradianceAtlasWidth;
        private int m_IrradianceAtlasHeight;
        private int m_DistanceAtlasWidth;
        private int m_DistanceAtlasHeight;
        private int m_ProbesPerRow;

        private RenderTexture[] m_IrradianceAtlases = new RenderTexture[2];
        private RenderTexture[] m_DistanceAtlases = new RenderTexture[2];
        private int m_CurrentBufferIndex = 0;

        private bool m_IsInitialized;

        #endregion

        #region Properties

        public RenderTexture CurrentIrradianceAtlas => m_IrradianceAtlases[m_CurrentBufferIndex];

        public RenderTexture CurrentDistanceAtlas => m_DistanceAtlases[m_CurrentBufferIndex];

        public RenderTexture PrevIrradianceAtlas => m_IrradianceAtlases[1 - m_CurrentBufferIndex];

        public RenderTexture PrevDistanceAtlas => m_DistanceAtlases[1 - m_CurrentBufferIndex];

        public RenderTexture IrradianceAtlas => CurrentIrradianceAtlas;

        public RenderTexture DistanceAtlas => CurrentDistanceAtlas;

        public RenderTexture IrradianceAtlasPrev => PrevIrradianceAtlas;

        public RenderTexture DistanceAtlasPrev => PrevDistanceAtlas;

        public int CurrentBufferIndex => m_CurrentBufferIndex;

        public bool IsInitialized => m_IsInitialized;

        public DDGIAtlasConfig Config => m_Config;

        public int ProbesPerRow => m_ProbesPerRow;

        public Vector2Int IrradianceAtlasSize => new Vector2Int(m_IrradianceAtlasWidth, m_IrradianceAtlasHeight);

        public Vector2Int DistanceAtlasSize => new Vector2Int(m_DistanceAtlasWidth, m_DistanceAtlasHeight);

        #endregion

        #region Constructor

        public DDGIAtlasManager()
        {
            m_Config = DDGIAtlasConfig.Default;
            m_IsInitialized = false;
        }

        public DDGIAtlasManager(DDGIAtlasConfig config)
        {
            m_Config = config;
            m_IsInitialized = false;
        }

        #endregion

        #region Public Methods

        public void Initialize(int probeCount)
        {
            if (m_IsInitialized && m_ProbeCount == probeCount)
                return;

            Cleanup();

            m_ProbeCount = probeCount;

            CalculateAtlasLayout();

            CreateTextures();

            m_IsInitialized = true;
        }

        public void Cleanup()
        {
            for (int i = 0; i < 2; i++)
            {
                if (m_IrradianceAtlases[i] != null)
                {
                    m_IrradianceAtlases[i].Release();
                    UnityEngine.Object.DestroyImmediate(m_IrradianceAtlases[i]);
                    m_IrradianceAtlases[i] = null;
                }

                if (m_DistanceAtlases[i] != null)
                {
                    m_DistanceAtlases[i].Release();
                    UnityEngine.Object.DestroyImmediate(m_DistanceAtlases[i]);
                    m_DistanceAtlases[i] = null;
                }
            }

            m_CurrentBufferIndex = 0;
            m_IsInitialized = false;
        }

        public Vector2 GetIrradianceProbeUV(int probeIndex)
        {
            Vector2Int pixelCoord = GetIrradianceProbePixelCoord(probeIndex);
            return new Vector2(
                (float)pixelCoord.x / m_IrradianceAtlasWidth,
                (float)pixelCoord.y / m_IrradianceAtlasHeight
            );
        }

        public Vector2 GetDistanceProbeUV(int probeIndex)
        {
            Vector2Int pixelCoord = GetDistanceProbePixelCoord(probeIndex);
            return new Vector2(
                (float)pixelCoord.x / m_DistanceAtlasWidth,
                (float)pixelCoord.y / m_DistanceAtlasHeight
            );
        }

        public Vector2Int GetIrradianceProbePixelCoord(int probeIndex)
        {
            int probeX = probeIndex % m_ProbesPerRow;
            int probeY = probeIndex / m_ProbesPerRow;

            int probeSize = m_Config.IrradianceProbeSizeWithGutter;

            return new Vector2Int(
                probeX * probeSize + m_Config.gutterSize,
                probeY * probeSize + m_Config.gutterSize
            );
        }

        public Vector2Int GetDistanceProbePixelCoord(int probeIndex)
        {
            int probeX = probeIndex % m_ProbesPerRow;
            int probeY = probeIndex / m_ProbesPerRow;

            int probeSize = m_Config.DistanceProbeSizeWithGutter;

            return new Vector2Int(
                probeX * probeSize + m_Config.gutterSize,
                probeY * probeSize + m_Config.gutterSize
            );
        }

        public void UpdateProbeAtlasUVs(System.Collections.Generic.IList<DDGIProbe> probes)
        {
            for (int i = 0; i < probes.Count; i++)
            {
                probes[i].SetAtlasUV(
                    GetIrradianceProbeUV(i),
                    GetDistanceProbeUV(i)
                );
            }
        }

        public Vector4 GetIrradianceAtlasParams()
        {

            return new Vector4(
                1.0f / m_IrradianceAtlasWidth,
                1.0f / m_IrradianceAtlasHeight,
                m_Config.irradianceProbeResolution,
                m_ProbesPerRow
            );
        }

        public Vector4 GetDistanceAtlasParams()
        {
            return new Vector4(
                1.0f / m_DistanceAtlasWidth,
                1.0f / m_DistanceAtlasHeight,
                m_Config.distanceProbeResolution,
                m_ProbesPerRow
            );
        }

        public void ClearAtlases(CommandBuffer cmd = null)
        {
            if (!m_IsInitialized)
                return;

            if (cmd != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    cmd.SetRenderTarget(m_IrradianceAtlases[i]);
                    cmd.ClearRenderTarget(false, true, Color.black);
                    cmd.SetRenderTarget(m_DistanceAtlases[i]);
                    cmd.ClearRenderTarget(false, true, new Color(1000f, 1000000f, 0, 0));
                }
            }
            else
            {
                RenderTexture prev = RenderTexture.active;

                for (int i = 0; i < 2; i++)
                {
                    RenderTexture.active = m_IrradianceAtlases[i];
                    GL.Clear(false, true, Color.black);

                    RenderTexture.active = m_DistanceAtlases[i];
                    GL.Clear(false, true, new Color(1000f, 1000000f, 0, 0));
                }

                RenderTexture.active = prev;
            }
        }

        public void SwapBuffers()
        {
            m_CurrentBufferIndex = 1 - m_CurrentBufferIndex;
        }

        #endregion

        #region Private Methods

        private void CalculateAtlasLayout()
        {

            m_ProbesPerRow = Mathf.CeilToInt(Mathf.Sqrt(m_ProbeCount));

            m_ProbesPerRow = Mathf.Max(1, m_ProbesPerRow);

            int probesPerCol = Mathf.CeilToInt((float)m_ProbeCount / m_ProbesPerRow);

            int irradianceProbeSize = m_Config.IrradianceProbeSizeWithGutter;
            m_IrradianceAtlasWidth = m_ProbesPerRow * irradianceProbeSize;
            m_IrradianceAtlasHeight = probesPerCol * irradianceProbeSize;

            int distanceProbeSize = m_Config.DistanceProbeSizeWithGutter;
            m_DistanceAtlasWidth = m_ProbesPerRow * distanceProbeSize;
            m_DistanceAtlasHeight = probesPerCol * distanceProbeSize;

            m_IrradianceAtlasWidth = Mathf.Max(irradianceProbeSize, m_IrradianceAtlasWidth);
            m_IrradianceAtlasHeight = Mathf.Max(irradianceProbeSize, m_IrradianceAtlasHeight);
            m_DistanceAtlasWidth = Mathf.Max(distanceProbeSize, m_DistanceAtlasWidth);
            m_DistanceAtlasHeight = Mathf.Max(distanceProbeSize, m_DistanceAtlasHeight);

            Debug.Log($"[DDGIAtlasManager] Atlas Layout: {m_ProbeCount} probes, {m_ProbesPerRow} per row\n" +
                      $"  Irradiance Atlas: {m_IrradianceAtlasWidth}x{m_IrradianceAtlasHeight}\n" +
                      $"  Distance Atlas: {m_DistanceAtlasWidth}x{m_DistanceAtlasHeight}");
        }

        private void CreateTextures()
        {
            for (int i = 0; i < 2; i++)
            {

                m_IrradianceAtlases[i] = new RenderTexture(
                    m_IrradianceAtlasWidth,
                    m_IrradianceAtlasHeight,
                    0,
                    IrradianceFormat,
                    RenderTextureReadWrite.Linear
                );
                m_IrradianceAtlases[i].name = $"DDGI_IrradianceAtlas_{i}";
                m_IrradianceAtlases[i].enableRandomWrite = true;
                m_IrradianceAtlases[i].filterMode = FilterMode.Bilinear;
                m_IrradianceAtlases[i].wrapMode = TextureWrapMode.Clamp;
                m_IrradianceAtlases[i].Create();

                m_DistanceAtlases[i] = new RenderTexture(
                    m_DistanceAtlasWidth,
                    m_DistanceAtlasHeight,
                    0,
                    DistanceFormat,
                    RenderTextureReadWrite.Linear
                );
                m_DistanceAtlases[i].name = $"DDGI_DistanceAtlas_{i}";
                m_DistanceAtlases[i].enableRandomWrite = true;
                m_DistanceAtlases[i].filterMode = FilterMode.Bilinear;
                m_DistanceAtlases[i].wrapMode = TextureWrapMode.Clamp;
                m_DistanceAtlases[i].Create();
            }

            ClearAtlases();

            Debug.Log($"[DDGIAtlasManager] Created Ping-Pong Atlas textures:\n" +
                      $"  Irradiance: {m_IrradianceAtlases[0].width}x{m_IrradianceAtlases[0].height} ({IrradianceFormat})\n" +
                      $"  Distance: {m_DistanceAtlases[0].width}x{m_DistanceAtlases[0].height} ({DistanceFormat})");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Cleanup();
        }

        #endregion
    }
}
