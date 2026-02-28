using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace URPSSGI
{

    public sealed class SSGIHistoryManager : IDisposable
    {

        public const int HistoryHF = 0;

        public const int HistoryLF = 1;

        public const int HistoryDepth = 2;

        public const int HistoryNormal = 3;

        public const int HistoryMotionVector = 4;

        private const int k_BufferTypeCount = 5;
        private const int k_FrameCount = 2;

        private readonly RenderTexture[,] m_Buffers = new RenderTexture[k_BufferTypeCount, k_FrameCount];

        private int m_CurrentWidth;
        private int m_CurrentHeight;
        private bool m_Allocated;

        private Matrix4x4 m_PrevViewMatrix = Matrix4x4.identity;
        private Matrix4x4 m_PrevGpuProjMatrix = Matrix4x4.identity;

        public Matrix4x4 PrevViewMatrix => m_PrevViewMatrix;

        public Matrix4x4 PrevGpuProjMatrix => m_PrevGpuProjMatrix;

        public void SetCurrentViewAndProj(Matrix4x4 view, Matrix4x4 gpuProj)
        {
            m_PrevViewMatrix = view;
            m_PrevGpuProjMatrix = gpuProj;
        }

        private float m_PrevExposure = 1.0f;

        public float PrevExposure => m_PrevExposure;

        public void SetCurrentExposure(float exposure) { m_PrevExposure = exposure; }

        private static readonly Dictionary<int, SSGIHistoryManager> s_Instances
            = new Dictionary<int, SSGIHistoryManager>();

        private SSGIHistoryManager() { }

        public RenderTexture GetCurrentFrame(int id)
        {
            return m_Buffers[id, 0];
        }

        public RenderTexture GetPreviousFrame(int id)
        {
            return m_Buffers[id, 1];
        }

        public void AllocateBuffersIfNeeded(int width, int height)
        {
            if (m_Allocated && m_CurrentWidth == width && m_CurrentHeight == height)
                return;

            if (m_Allocated)
                ReleaseBuffers();

            AllocDoubleBuffer(HistoryHF, width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "SSGIHistoryHF");

            AllocDoubleBuffer(HistoryLF, width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "SSGIHistoryLF");

            AllocDoubleBuffer(HistoryDepth, width, height,
                GraphicsFormat.R32_SFloat, true, "SSGIHistoryDepth");

            AllocDoubleBuffer(HistoryNormal, width, height,
                GraphicsFormat.R16G16B16A16_SFloat, true, "SSGIHistoryNormal");

            AllocDoubleBuffer(HistoryMotionVector, width, height,
                GraphicsFormat.R16G16_SFloat, true, "SSGIHistoryMotionVector");

            m_CurrentWidth = width;
            m_CurrentHeight = height;
            m_Allocated = true;
        }

        public void SwapAndSetReferenceSize(int width, int height)
        {

            for (int i = 0; i < k_BufferTypeCount; i++)
            {
                var temp = m_Buffers[i, 0];
                m_Buffers[i, 0] = m_Buffers[i, 1];
                m_Buffers[i, 1] = temp;
            }
        }

        public void Dispose()
        {
            ReleaseBuffers();
        }

        private void AllocDoubleBuffer(int bufferId, int width, int height,
            GraphicsFormat format, bool enableRandomWrite, string baseName)
        {
            for (int frame = 0; frame < k_FrameCount; frame++)
            {
                var desc = new RenderTextureDescriptor(width, height)
                {
                    graphicsFormat = format,
                    depthBufferBits = 0,
                    useMipMap = false,
                    autoGenerateMips = false,
                    enableRandomWrite = enableRandomWrite,
                    msaaSamples = 1,
                    dimension = UnityEngine.Rendering.TextureDimension.Tex2D
                };
                var rt = new RenderTexture(desc);
                rt.name = baseName + "_" + frame;
                rt.Create();
                m_Buffers[bufferId, frame] = rt;
            }
        }

        private void ReleaseBuffers()
        {
            for (int i = 0; i < k_BufferTypeCount; i++)
            {
                for (int f = 0; f < k_FrameCount; f++)
                {
                    if (m_Buffers[i, f] != null)
                    {
                        m_Buffers[i, f].Release();
                        UnityEngine.Object.DestroyImmediate(m_Buffers[i, f]);
                        m_Buffers[i, f] = null;
                    }
                }
            }
            m_Allocated = false;
        }

        public static SSGIHistoryManager GetOrCreate(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (s_Instances.TryGetValue(id, out SSGIHistoryManager manager))
                return manager;

            manager = new SSGIHistoryManager();
            s_Instances.Add(id, manager);
            return manager;
        }

        public static void Release(Camera camera)
        {
            int id = camera.GetInstanceID();
            if (s_Instances.TryGetValue(id, out SSGIHistoryManager manager))
            {
                manager.Dispose();
                s_Instances.Remove(id);
            }
        }

        public static void ReleaseAll()
        {
            foreach (var kvp in s_Instances)
                kvp.Value.Dispose();
            s_Instances.Clear();
        }
    }
}
