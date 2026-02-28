using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace URPSSGI
{

    internal static class URPTextureResolver
    {

        private static bool s_Initialized;
        private static bool s_ReflectionFailed;

        private static FieldInfo s_DepthTextureField;
        private static FieldInfo s_NormalsTextureField;
        private static FieldInfo s_OpaqueColorField;
        private static FieldInfo s_MotionVectorColorField;
        private static FieldInfo s_DeferredLightsField;

        private static PropertyInfo s_GbufferAttachmentsProp;
        private static PropertyInfo s_GBufferNormalIndexProp;

        private static readonly int s_CameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");
        private static readonly int s_CameraNormalsTextureID = Shader.PropertyToID("_CameraNormalsTexture");
        private static readonly int s_CameraOpaqueTextureID = Shader.PropertyToID("_CameraOpaqueTexture");
        private static readonly int s_CameraMotionVectorsTextureID = Shader.PropertyToID("_CameraMotionVectorsTexture");
        private static readonly int s_MotionVectorTextureID = Shader.PropertyToID("_MotionVectorTexture");
        private static readonly int s_GBuffer0ID = Shader.PropertyToID("_GBuffer0");
        private static readonly int s_GBuffer1ID = Shader.PropertyToID("_GBuffer1");
        private static readonly int s_GBuffer2ID = Shader.PropertyToID("_GBuffer2");

        private static bool s_LoggedNormalsFallback;
        private static bool s_LoggedGBufferFallback;

        private static void Initialize()
        {
            if (s_Initialized) return;
            s_Initialized = true;

            try
            {
                var rendererType = typeof(UniversalRenderer);
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;

                s_DepthTextureField = rendererType.GetField("m_DepthTexture", flags);
                s_NormalsTextureField = rendererType.GetField("m_NormalsTexture", flags);
                s_OpaqueColorField = rendererType.GetField("m_OpaqueColor", flags);
                s_MotionVectorColorField = rendererType.GetField("m_MotionVectorColor", flags);
                s_DeferredLightsField = rendererType.GetField("m_DeferredLights", flags);

                var deferredType = typeof(UniversalRenderer).Assembly.GetType(
                    "UnityEngine.Rendering.Universal.Internal.DeferredLights");
                if (deferredType != null)
                {
                    s_GbufferAttachmentsProp = deferredType.GetProperty("GbufferAttachments",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    s_GBufferNormalIndexProp = deferredType.GetProperty("GBufferNormalSmoothnessIndex",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (s_DepthTextureField == null)
                    Debug.LogWarning("[SSGI] URPTextureResolver: m_DepthTexture 未找到");
                if (s_DeferredLightsField == null)
                    Debug.LogWarning("[SSGI] URPTextureResolver: m_DeferredLights 未找到");
                if (deferredType == null)
                    Debug.LogWarning("[SSGI] URPTextureResolver: DeferredLights 类型未找到");
                else if (s_GbufferAttachmentsProp == null)
                    Debug.LogWarning("[SSGI] URPTextureResolver: GbufferAttachments 属性未找到");

                if (s_DepthTextureField == null)
                    s_ReflectionFailed = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SSGI] URPTextureResolver 反射初始化失败: {e.Message}");
                s_ReflectionFailed = true;
            }
        }

        private static Texture GetRTHandleTexture(object obj)
        {
            if (obj == null) return null;
            return (obj as RTHandle)?.rt;
        }

        private static object GetDeferredLights(ScriptableRenderer renderer)
        {
            if (s_DeferredLightsField == null) return null;
            return s_DeferredLightsField.GetValue(renderer);
        }

        private static RTHandle[] GetGbufferAttachments(object deferredLights)
        {
            if (deferredLights == null || s_GbufferAttachmentsProp == null) return null;
            return s_GbufferAttachmentsProp.GetValue(deferredLights) as RTHandle[];
        }

        public static Texture ResolveDepthTexture(ScriptableRenderer renderer)
        {
            Initialize();
            if (!s_ReflectionFailed && s_DepthTextureField != null && renderer is UniversalRenderer)
            {
                var tex = GetRTHandleTexture(s_DepthTextureField.GetValue(renderer));
                if (tex != null) return tex;
            }
            return Shader.GetGlobalTexture(s_CameraDepthTextureID);
        }

        public static Texture ResolveNormalsTexture(ScriptableRenderer renderer)
        {
            Initialize();
            if (!s_ReflectionFailed && renderer is UniversalRenderer)
            {

                var deferredLights = GetDeferredLights(renderer);
                if (deferredLights != null)
                {
                    var attachments = GetGbufferAttachments(deferredLights);
                    if (attachments != null && s_GBufferNormalIndexProp != null)
                    {
                        int normalIndex = (int)s_GBufferNormalIndexProp.GetValue(deferredLights);
                        if (normalIndex >= 0 && normalIndex < attachments.Length)
                        {
                            var tex = attachments[normalIndex]?.rt;
                            if (tex != null) return tex;
                        }
                    }
                }

                if (s_NormalsTextureField != null)
                {
                    var tex = GetRTHandleTexture(s_NormalsTextureField.GetValue(renderer));
                    if (tex != null) return tex;
                }

                if (!s_LoggedNormalsFallback)
                {
                    s_LoggedNormalsFallback = true;
                    Debug.LogWarning("[SSGI] URPTextureResolver: 法线纹理反射解析失败，" +
                        $"deferredLights={(deferredLights != null ? "有" : "无")}, " +
                        $"GbufferAttachments={(GetGbufferAttachments(deferredLights) != null ? "有" : "无")}, " +
                        $"m_NormalsTexture={(s_NormalsTextureField != null ? "有字段" : "无字段")}，" +
                        "回退到 Shader.GetGlobalTexture");
                }
            }
            return Shader.GetGlobalTexture(s_CameraNormalsTextureID);
        }

        public static Texture ResolveOpaqueTexture(ScriptableRenderer renderer)
        {
            Initialize();
            if (!s_ReflectionFailed && s_OpaqueColorField != null && renderer is UniversalRenderer)
            {
                var tex = GetRTHandleTexture(s_OpaqueColorField.GetValue(renderer));
                if (tex != null) return tex;
            }
            return Shader.GetGlobalTexture(s_CameraOpaqueTextureID);
        }

        public static Texture ResolveMotionVectorsTexture()
        {

            Texture tex = Shader.GetGlobalTexture(s_MotionVectorTextureID);
            if (tex != null) return tex;
            return Shader.GetGlobalTexture(s_CameraMotionVectorsTextureID);
        }

        public static Texture ResolveMotionVectorsTexture(ScriptableRenderer renderer)
        {
            Initialize();
            if (!s_ReflectionFailed && s_MotionVectorColorField != null && renderer is UniversalRenderer)
            {
                var tex = GetRTHandleTexture(s_MotionVectorColorField.GetValue(renderer));
                if (tex != null) return tex;
            }
            return ResolveMotionVectorsTexture();
        }

        public static bool ResolveGBuffers(ScriptableRenderer renderer,
            out Texture gb0, out Texture gb1, out Texture gb2)
        {
            gb0 = gb1 = gb2 = null;
            Initialize();

            if (!s_ReflectionFailed && renderer is UniversalRenderer)
            {
                var deferredLights = GetDeferredLights(renderer);
                var attachments = GetGbufferAttachments(deferredLights);
                if (attachments != null && attachments.Length >= 3)
                {
                    gb0 = attachments[0]?.rt;
                    gb1 = attachments[1]?.rt;
                    gb2 = attachments[2]?.rt;
                    if (gb0 != null && gb1 != null && gb2 != null)
                        return true;
                }

                if (!s_LoggedGBufferFallback)
                {
                    s_LoggedGBufferFallback = true;
                    int len = attachments?.Length ?? -1;
                    Texture t0 = len >= 1 ? attachments[0]?.rt : null;
                    Texture t1 = len >= 2 ? attachments[1]?.rt : null;
                    Texture t2 = len >= 3 ? attachments[2]?.rt : null;
                    Debug.LogWarning("[SSGI] URPTextureResolver: GBuffer 反射解析失败，" +
                        $"deferredLights={(deferredLights != null ? "有" : "无")}, " +
                        $"attachments.Length={len}, " +
                        $"rt[0]={(t0 != null ? t0.name : "null")}, " +
                        $"rt[1]={(t1 != null ? t1.name : "null")}, " +
                        $"rt[2]={(t2 != null ? t2.name : "null")}，" +
                        "回退到 Shader.GetGlobalTexture");
                }
            }

            gb0 = Shader.GetGlobalTexture(s_GBuffer0ID);
            gb1 = Shader.GetGlobalTexture(s_GBuffer1ID);
            gb2 = Shader.GetGlobalTexture(s_GBuffer2ID);
            return gb0 != null && gb1 != null && gb2 != null;
        }
    }
}
