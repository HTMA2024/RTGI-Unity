# URP SSGI 项目综合文档

## 一、项目概述

本项目是一个基于 Unity URP 14.0.12 的全局光照（Global Illumination）系统实现，由斯基编写。核心功能是将 HDRP 的 SSGI（Screen Space Global Illumination）和 RTGI（Ray Traced Global Illumination）迁移到 URP Deferred 渲染管线，并在此基础上进行了大量架构优化和功能扩展。

项目支持三种 GI 模式：
- **ScreenSpace（SSGI）**：基于 Hi-Z Ray Marching 的屏幕空间全局光照
- **RayTraced（RTGI）**：基于 DXR 硬件光线追踪的全局光照
- **Mixed**：SSGI + RTGI 互补模式，SSGI 处理屏幕空间可见区域，RTGI 回退处理未命中区域

项目同时包含一个第三方参考实现 SSRT3（基于 GTAO 半球切片采样），以及对 HDRP 14.0.12 和 URP 14.0.12 包的局部修改。

## 二、技术架构

### 2.1 渲染管线流程

```
ScreenSpace 模式:
  DepthPyramid → ColorPyramid → Trace(Hi-Z) → Reproject(ColorPyramid采样)
  → TemporalFilter → SpatialDenoise → [BilateralUpsample] → Composite

RayTraced 模式:
  RTAS Update → DispatchRays(DXR) → TemporalFilter → SpatialDenoise
  → [BilateralUpsample] → Composite

Mixed 模式:
  SSGIRenderPass:
    DepthPyramid → Trace → Reproject(GBuffer收集) → DeferredLighting
    → 输出 SSGIResult + HitValidityMask
  RTGIRenderPass:
    ClearGBufferValidity → DispatchRays(仅mask=0像素) → DeferredLighting
    → Merge(SSGI+RTGI) → 统一去噪(Temporal+Spatial×2) → [RTAO去噪]
    → [BilateralUpsample] → Composite
```

### 2.2 关键技术特性

- **Mip Atlas 方案**：所有 mip 级别打包在单张 RWTexture2D 中，通过偏移地址访问，避免 URP 下 mip chain UAV 绑定失败
- **BND 序列采样**：Owen scrambled Sobol 序列 + per-pixel ranking/scrambling，产生蓝噪声分布的低差异采样
- **Camera Relative Rendering**：VP/InvVP 矩阵使用 Camera Relative 变换，避免远距离浮点精度问题
- **世界空间圆盘采样降噪**：自适应滤波半径 + 三重双边权重（深度/法线/平面），替代传统屏幕空间分离式双边滤波
- **Mixed 模式统一 Deferred Lighting**：SSGI 和 RTGI 都走 GBuffer 收集 + 延迟光照路径，消除 ColorPyramid 与 CHS 之间的色差
- **RTAO 互补射线**：Mixed 模式下 SSGI 命中像素仍独立发射 RTAO 射线，确保 AO 连续性
- **去噪 Dispatch 合并**：TemporalFilter 双写合并 CopyHistory + Merge-On-Read 合并 MergeSSGIAndRTGI，减少 5 个全屏 dispatch

### 2.3 调试可视化（20 种模式）

None / GIOnly / HitPointUV / DepthPyramid / WorldNormal / AccumulationCount / DenoiseComparison / RawGI / RayDirection / HitValidity / MotionVector / ReprojectUV / DenoisedGI / RTGIOnly / RTGIRayLength / MixedMask / RTGINormal / RTAO / RTGIWithAO / RTGIShadowMap

## 三、目录结构与文件索引

### 3.1 URPSSGI/Runtime/ — C# 运行时核心

| 文件 | 类型 | 功能 |
|------|------|------|
| `SSGIRendererFeature.cs` | ScriptableRendererFeature | 系统入口，模式路由（ScreenSpace/RayTraced/Mixed），管理所有 Pass 生命周期 |
| `SSGIRenderPass.cs` | ScriptableRenderPass | SSGI 核心渲染 Pass：RT 分配、金字塔生成、Compute Shader 调度、历史管理、去噪编排 |
| `RTGIRenderPass.cs` | ScriptableRenderPass | RTGI 渲染 Pass：DXR DispatchRays、Mixed 模式 Merge、统一去噪、RTAO 去噪、最终输出 |
| `SSGICompositePass.cs` | ScriptableRenderPass | GI 合成 Pass：间接光照叠加到场景颜色（含 albedo 乘算、调试可视化、RTAO 混合） |
| `RTASManager.cs` | sealed class | 光线追踪加速结构管理：RTAS 创建/构建/更新/释放，LayerMask 过滤 |
| `SSGICameraContext.cs` | sealed class | Per-camera RT 资源管理器：所有 RT 的分配/释放/双缓冲交换，解决多窗口 RT 冲突 |
| `SSGIHistoryManager.cs` | IDisposable | 帧历史缓冲管理器：double-buffer（HF/LF/Depth/Normal/AccumCount），per-camera 实例 |
| `SSGIVolumeComponent.cs` | VolumeComponent | Volume 参数定义：光线步进、去噪、合成、调试、RTGI、RTAO 全部参数 |
| `SSGIShaderIDs.cs` | static class | Shader 属性 ID 集中管理，静态构造一次性初始化 |
| `SSGITypes.cs` | struct/enum | PackedMipChainInfo（Atlas 布局）、枚举定义（IndirectDiffuseMode/FallbackHierarchy/CompositeMode/DebugMode） |
| `SSGIRuntimeStats.cs` | struct | 运行时统计数据（readonly struct，供 Editor 读取） |
| `SSGIBilateralUpsampleDef.cs` | static class | 双边上采样预计算权重数据（从 HDRP 提取） |
| `DepthPyramidGenerator.cs` | sealed class | 深度金字塔生成器：min-depth 逐级降采样到 Mip Atlas |
| `ColorPyramidGenerator.cs` | sealed class | 颜色金字塔生成器：LDS 优化高斯模糊降采样到 Mip Atlas |

### 3.2 URPSSGI/Shaders/ — Shader 文件

| 文件 | 类型 | 功能 |
|------|------|------|
| `SSGI.compute` | Compute Shader | 核心 SSGI：Trace（Hi-Z Ray March）+ Reproject（颜色金字塔采样 / GBuffer 收集） |
| `SSGITemporalFilter.compute` | Compute Shader | 时间域去噪：TemporalFilter（AABB Clamp + 运动向量重投影 + 深度/法线验证）+ CopyHistory + CopyNormals + Merge-On-Read |
| `SSGIDiffuseDenoiser.compute` | Compute Shader | 空间域去噪：世界空间圆盘采样 BilateralFilterColor + GeneratePointDistribution |
| `SSGIDeferredLighting.compute` | Compute Shader | 延迟光照：读取 SSGI GBuffer，评估 URP 主光源 + Additional Lights，输出 lit 颜色 + Merge kernel |
| `SSGIBilateralUpsample.compute` | Compute Shader | 双边上采样：Gather + 深度加权双边滤波 |
| `DepthPyramid.compute` | Compute Shader | 深度金字塔：KDepthCopyMip0 + KDepthDownsample |
| `ColorPyramid.compute` | Compute Shader | 颜色金字塔：KCopyMip0 + KColorGaussian + KColorDownsample |
| `SSGIComposite.shader` | Fragment Shader | GI 合成 + 调试可视化（20 种调试模式） |
| `RTGIIndirectDiffuse.raytrace` | RayTracing Shader | DXR RayGen + Miss：余弦加权半球采样、BND 序列、Mixed 模式 GBuffer 输出、RTAO 互补射线 |
| `RTGILit.shader` | Surface Shader | DXR Closest Hit Shader：命中点光照计算 + GBuffer 参数输出（albedo/normal/smoothness） |
| `SSGICommon.hlsl` | HLSL Include | 公共工具：CBuffer、法线解码、BND 序列采样、蓝噪声、屏幕边缘淡出 |
| `SSGIRayMarching.hlsl` | HLSL Include | Hi-Z Ray Marching 核心算法（Mip Atlas 采样） |
| `SSGITypes.hlsl` | HLSL Include | HLSL 侧枚举定义（与 C# SSGITypes.cs 对齐） |
| `SSGIBilateralUpsample.hlsl` | HLSL Include | 双边上采样核心算法（深度加权） |
| `URPLit.shader` | Surface Shader | URP Lit 参考/修改版 |

### 3.3 URPSSGI/Editor/ — 编辑器代码

| 文件 | 功能 |
|------|------|
| `SSGIVolumeComponentEditor.cs` | Volume 组件自定义 Inspector：分组折叠、条件显示、运行时统计面板、模式路由 UI |

### 3.4 URPSSGI/Editor/Tests/ — 单元测试

| 文件 | 功能 |
|------|------|
| `WorldSpaceSpatialDenoiseTests.cs` | 世界空间圆盘采样降噪属性测试 + 单元测试 |
| `SSGIDenoiseTests.cs` | 去噪系统通用测试 |
| `SSGICameraContextTests.cs` | Per-camera RT 资源管理测试 |
| `SSGIDeferredLightingTests.cs` | Deferred Lighting 正确性测试 |
| `RTGIQualityTests.cs` | RTGI Quality 模式测试 |
| `RTAOTests.cs` | RTAO 基础功能测试 |
| `RTAOComplementaryRayTests.cs` | RTAO 互补射线 bugfix 测试 |
| `MixedModeInvarianceTests.cs` | Mixed 模式行为不变性测试 |
| `MixedDispatchCountTests.cs` | Mixed 模式 Dispatch 计数测试 |
| `BilateralFireflyTests.cs` | 双边滤波萤火虫抑制测试 |

### 3.5 URPSSGI/Textures/ — 纹理资源

| 文件 | 功能 |
|------|------|
| `OwenScrambledNoise256.png` | Owen Scrambled Sobol 蓝噪声纹理（256×256） |
| `ScramblingTile8SPP.png` | 8SPP Scrambling Tile 纹理 |
| `RankingTile8SPP.png` | 8SPP Ranking Tile 纹理 |
| `LDR_RGBA_0.png` | 蓝噪声 LDR 纹理 |

### 3.6 URPSSGI/Docs/ — 项目文档

| 文件 | 功能 |
|------|------|
| `SSGI_System_Overview.md` | URP SSGI 系统架构文档（组件清单、渲染流程、Volume 参数） |

### 3.7 Docs/ — 迁移与对比文档

| 文件 | 功能 |
|------|------|
| `SSGI_HDRP_to_URP_Migration_Plan.md` | HDRP SSGI → URP 完整迁移计划（文件清单、依赖分析、应对策略、迁移评估） |
| `SSGI_vs_SSRT3_Technical_Comparison.md` | HDRP SSGI vs SSRT3 技术对比（算法、架构、性能、画质、迁移难度） |
| `SSRT3_HDRP_to_URP_Migration_Plan.md` | SSRT3 → URP 迁移计划（文件清单、依赖分析、应对策略） |

### 3.8 SSRT3/ — 第三方参考实现

| 路径 | 功能 |
|------|------|
| `SSRT3/Scripts/SSRT_HDRP.cs` | SSRT3 主控制组件（HDRP CustomPostProcessVolumeComponent） |
| `SSRT3/Shaders/Resources/` | SSRT3 Shader 资源（SSRTCS.compute + SSRT.shader + DiffuseDenoiser.compute） |
| `SSRT3/Textures/Resources/` | SSRT3 蓝噪声纹理 |
| `SSRT3/Classroom/` | 教室演示场景 |

### 3.9 Unity 包修改

| 路径 | 功能 |
|------|------|
| `com.unity.render-pipelines.high-definition@14.0.12/` | HDRP 14.0.12 包（含 SSGI 参考源码和 SSGIDebugMode 扩展） |
| `com.unity.render-pipelines.universal@14.0.12/` | URP 14.0.12 包（含 DeferredLights.cs 修改） |
| `com.unity.render-pipelines.high-definition-config@14.0.12/` | HDRP Config 包 |

### 3.10 其他目录

| 路径 | 功能 |
|------|------|
| `Scenes/` | Unity 场景文件 |
| `SponzaURPScenes/` | Sponza URP 测试场景 |
| `Materials/` | 材质资源 |
| `SampleSceneAssets/` | 示例场景资源 |
| `HDRPDefaultResources/` | HDRP 默认资源 |
| `DebugLog/` | 调试日志 |
| `com.unity.sponza-urp@5665fb87d0/` | Sponza URP 场景包 |



## 四、Spec 开发历史索引

项目通过 Kiro Spec 系统进行迭代开发，以下是所有 Spec 及其对应的功能模块：

| Spec 目录 | 功能描述 | 涉及的核心文件 |
|-----------|---------|---------------|
| `ssgi-urp-infrastructure` | URP 基础设施搭建：RendererFeature + RenderPass + VolumeComponent + HistoryManager 框架 | SSGIRendererFeature.cs, SSGIRenderPass.cs, SSGIVolumeComponent.cs, SSGIHistoryManager.cs, SSGIShaderIDs.cs, SSGITypes.cs |
| `ssgi-pyramid-generators` | 深度金字塔和颜色金字塔生成器实现（Mip Atlas 方案） | DepthPyramid.compute, ColorPyramid.compute, DepthPyramidGenerator.cs, ColorPyramidGenerator.cs |
| `ssgi-core-shaders` | 核心 SSGI Shader 迁移：Hi-Z Ray Marching + Reproject + 蓝噪声采样 | SSGI.compute, SSGIRayMarching.hlsl, SSGICommon.hlsl, SSGITypes.hlsl |
| `ssgi-pipeline-integration` | 完整管线集成：CBuffer 绑定、Compute 调度、双边上采样、帧索引管理 | SSGIRenderPass.cs, SSGIBilateralUpsample.compute/hlsl, SSGIBilateralUpsampleDef.cs |
| `ssgi-denoise-system` | 去噪系统迁移：时间域去噪（TemporalFilter）+ 空间域去噪（DiffuseDenoiser） | SSGITemporalFilter.compute, SSGIDiffuseDenoiser.compute, SSGIHistoryManager.cs |
| `ssgi-gi-injection` | GI 结果注入光照：CompositePass + Composite Shader + 天空遮罩 | SSGICompositePass.cs, SSGIComposite.shader |
| `ssgi-debug-visualization` | 调试可视化系统：20 种调试模式 + Editor UI 分组折叠 + 运行时统计 | SSGIComposite.shader, SSGIVolumeComponentEditor.cs, SSGIRuntimeStats.cs |
| `world-space-spatial-denoise` | 世界空间圆盘采样降噪：替代屏幕空间分离式双边滤波 | SSGIDiffuseDenoiser.compute |
| `hdrp-ssgi-debug-mode` | HDRP SSGI 调试模式参考实现 | SSGIDebugMode.cs, HDRenderPipeline.ScreenSpaceGlobalIllumination.cs |
| `hdrp-rtgi-quality` | HDRP 风格 RTGI Quality 模式：DXR 光线追踪 + RTAS 管理 + 混合模式 | RTGIRenderPass.cs, RTGIIndirectDiffuse.raytrace, RTGILit.shader, RTASManager.cs |
| `ssgi-deferred-lighting` | SSGI Deferred Lighting Bugfix：GBuffer 收集 + 延迟光照，消除 Mixed 模式色差 | SSGIDeferredLighting.compute, SSGI.compute, SSGICameraContext.cs |
| `mixed-mode-unified-pipeline` | Mixed 模式统一管线：SSGI+RTGI 共享 GBuffer + 统一去噪 + RTAO 混合 | SSGIRenderPass.cs, RTGIRenderPass.cs, RTGIIndirectDiffuse.raytrace, RTGILit.shader |
| `rtao-complementary-rays` | RTAO 互补射线 Bugfix：SSGI 命中像素独立发射 RTAO 射线 | RTGIIndirectDiffuse.raytrace |
| `denoise-dispatch-merge` | 去噪 Dispatch 合并优化：CopyHistory 双写 + Merge-On-Read | SSGITemporalFilter.compute, RTGIRenderPass.cs |
| `denoise-pipeline-optimization` | 去噪管线优化（与 denoise-dispatch-merge 相关） | SSGITemporalFilter.compute, SSGIDiffuseDenoiser.compute |
| `mixed-mode-perf-optimization` | Mixed 模式性能优化：ColorPyramid 释放、CopyNormals 消除、RT 按需分配、矩阵缓存 | SSGIRenderPass.cs, RTGIRenderPass.cs, SSGICameraContext.cs |

## 五、编程标准

项目遵循严格的编程标准（详见 `.kiro/steering/product.md`），核心要点：

### Shader 优化准则
- 不做防御性编程，假定输入合法
- 避免 if/else 动态分支，用数学等价变换
- `a * rcp(b)` 替代除法，`saturate(x)` 替代 `clamp(x,0,1)`
- 小整数次幂手动展开，表达式写成 MAD 形式
- 能在 VS 做的不放 PS，能 CPU 预计算的不放 Shader
- 使用内建函数（dot/mad/lerp），优先减少 ALU 和 Texture 指令数

### C# 优化准则
- 热路径零 GC Alloc，不在 Update 中 new
- 缓存所有频繁访问的引用（Component/Transform）
- 使用 for 替代 foreach，避免 LINQ
- 使用 struct 替代 class（明确生命周期时）
- 使用 sealed 类避免虚调用，readonly 提示编译器优化

### 实现规范
- 论文/PPT 实现严格遵照公式，代码上方写公式注释
- SurfelBasedRayTracingManager 的选项变更需同步 Editor 界面

## 六、Volume 参数总览

### 通用设置
- `enable` (bool, false) — 启用 SSGI
- `giMode` (IndirectDiffuseMode, ScreenSpace) — GI 模式
- `fullResolution` (bool, false) — 全分辨率/半分辨率

### 光线步进（ScreenSpace/Mixed）
- `maxRaySteps` (int [1,128], 32) — Hi-Z 最大步进次数
- `depthBufferThickness` (float [0.001,1], 0.1) — 深度缓冲厚度
- `rayMissFallback` (enum, ReflectionProbes) — 光线未命中回退策略

### 光线追踪（RayTraced/Mixed）
- `rtRayLength` (float, 50.0) — 光线最大长度
- `rtSampleCount` (int [1,32], 2) — 每像素采样数
- `rtBounceCount` (int [1,8], 1) — 弹射次数
- `rtClampValue` (float, 100.0) — 辐照度钳制值
- `rtTextureLodBias` (int [0,7], 7) — 纹理 LOD 偏移
- `rtMixedRaySteps` (int [0,128], 48) — Mixed 模式 SSGI 步数

### 去噪
- `denoise` (bool, true) — 启用去噪
- `denoiserRadius` (float [0.001,1], 0.5) — 去噪滤波半径
- `secondDenoiserPass` (bool, true) — 第二遍去噪

### GI 合成
- `compositeIntensity` (float [0,5], 1.0) — GI 合成强度
- `compositeMode` (enum, Additive) — 合成模式

### 调试
- `debugMode` (SSGIDebugMode, None) — 调试可视化模式（20 种）
- `debugMipLevel` (int [0,10], 0) — 深度金字塔调试 mip 级别

## 七、与另一个项目整合时的注意事项

1. **命名空间**：所有 URPSSGI 代码在 `URPSSGI` 命名空间下，整合时注意命名冲突
2. **Unity 包修改**：本项目修改了 URP 14.0.12 的 `DeferredLights.cs` 和 HDRP 14.0.12 的部分文件，整合时需要合并这些修改
3. **资源引用**：SSGIRendererFeature 通过 SerializeField 引用所有 Compute Shader 和纹理，整合后需要在 Inspector 中重新指定
4. **Per-camera 架构**：SSGICameraContext 和 SSGIHistoryManager 都是 per-camera 的 Dictionary 管理，支持多窗口（Scene + Game）
5. **Shader 关键字**：项目使用了多个 multi_compile 变体（SSGI_DEFERRED_LIGHTING、_MERGE_ON_READ 等），整合时注意关键字冲突
6. **DXR 依赖**：RTGI 和 Mixed 模式依赖 DXR 硬件光线追踪，不支持时自动回退到 ScreenSpace
7. **Steering 规则**：项目有严格的 Shader/C# 优化准则，新代码应遵循 `.kiro/steering/product.md` 中的标准
