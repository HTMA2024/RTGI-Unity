# URP Real-Time Global Illumination

基于 Unity URP 14.0.12 的实时全局光照系统，在 Deferred Rendering + DXR 管线上实现了两套独立的 GI 方案。

## 效果展示

![overview](Images/overview.gif)
![overview](Images/overview-1.gif)
![overview](Images/overview-2.gif)


![overview](Images/bistro.gif)
![overview](Images/bistro-gi.jpg)

### 模式对比

| SSGI (Screen Space) | RTGI (Ray Traced) | MixedDDGI | RTAO (Ray Traced) |
|:---:|:---:|:---:|:---:|
| ![ssgi](Images/ssgi.jpg) | ![rtgi](Images/rtgi.jpg) | ![mixed-ddgi](Images/mixed-ddgi.jpg) | ![rtao](Images/rtao.jpg) |

### GI 开关对比

| GI Off | GI On |
|:---:|:---:|
| ![gi-off](Images/gi-off.jpg) | ![gi-on](Images/gi-on.jpg) |

### DDGI 探针可视化

![probe-vis](Images/probe-viz.gif)

### Debug 可视化

| IndirectDiffuse | HitDistance | SSGI Mask |
|:---:|:---:|:---:|
| ![debug-indirect](Images/debug-indirect.jpg) | ![debug-hitdist](Images/debug-hitdist.jpg) | ![debug-mask](Images/debug-mask.jpg) |

## 系统架构

### URPSSGI (`Assets/URPSSGI/`)

支持四种 GI 模式：

- `ScreenSpace` — Hi-Z Ray Marching，纯屏幕空间追踪
- `RayTraced` — DXR 硬件光线追踪
- `Mixed` — SSGI 优先，未命中像素回退至 RTGI，miss shader 采样天空盒
- `MixedDDGI` — 与 Mixed 相同的混合策略，但 miss shader 改为采样 DDGI irradiance atlas

### DDGILightProbe (`Assets/DDGILightProbe/`)

参考 NVIDIA RTXGI SDK 实现的动态漫反射探针系统。在三维均匀网格上布置探针，每帧通过 DXR 发射光线并更新 irradiance/distance atlas。支持 probe relocation、classification、variability-based adaptive update，并集成了完整的漏光抑制机制（surface bias、wrap shading、Chebyshev visibility test、weight crushing）。

### 系统联动

两套系统可独立运行，也可通过 `MixedDDGI` 模式协同工作：共享同一 RTAS（每帧仅构建一次）、共享 Closest Hit Shader，DDGI 通过 `DDGIResourceProvider` 静态接口向 URPSSGI 暴露 atlas 资源。当 DDGI 未启用时，自动回退至标准 Mixed 模式。

## 渲染管线

```
URPSSGI ScreenSpace:
  DepthPyramid → ColorPyramid → Hi-Z Trace → Reproject
  → Temporal Filter → Spatial Filter → [Bilateral Upsample] → Composite

URPSSGI Mixed / MixedDDGI:
  SSGI: DepthPyramid → Trace → Reproject(GBuffer) → Deferred Lighting → result + mask
  RTGI: DispatchRays(mask=0) → Deferred Lighting → Merge → Denoise → Composite

DDGILightProbe (per frame):
  Ray Trace → G-Buffer → [Relocation] → [Classification]
  → LightingCombined (single pass) → MC Integration → Variability Reduction
  → Border Update → Ping-Pong Swap
```

## 关键技术实现

### URPSSGI

- Mip 层级打包至单张 `RWTexture2D`（Mip Atlas），规避 URP 下 mip chain 无法绑定为 UAV 的限制
- 空间降噪采用世界空间圆盘采样 + 自适应核半径，结合深度/法线/平面距离三重双边权重
- Mixed 模式下 SSGI 与 RTGI 统一经由 GBuffer → Deferred Lighting 路径着色，消除 ColorPyramid 与 Closest Hit Shader 之间的色调偏差
- Temporal Filter 内联写入历史缓冲 + Merge-On-Read 策略，减少 5 次全屏 dispatch
- 采样序列基于 Owen-scrambled Sobol + per-pixel ranking/scrambling（BND 序列）
- 全程 Camera Relative Rendering
- 内置 20 种 debug 可视化模式

### DDGILightProbe

- 直接光照、间接光照与 radiance 合成压缩至单个 compute pass（LightingCombined）
- Atlas 采用 ping-pong 双缓冲实现零拷贝交换
- Variability reduction 通过多级并行归约 + AsyncGPUReadback 驱动自适应更新频率
- 探针可视化支持 irradiance / distance / relocation offset / classification state / backface ratio 等模式

## 目录结构

```
Assets/
├── URPSSGI/
│   ├── Runtime/          SSGIRendererFeature, SSGIRenderPass, RTGIRenderPass,
│   │                     RTASManager, SSGICameraContext, SSGIHistoryManager ...
│   ├── Shaders/          SSGI.compute, SSGITemporalFilter.compute,
│   │                     SSGIDiffuseDenoiser.compute, RTGIIndirectDiffuse.raytrace,
│   │                     UnifiedClosestHit.hlsl ...
│   ├── Editor/           Inspector + Unit Tests
│   └── Textures/         Blue Noise Textures
│
├── DDGILightProbe/
│   ├── Runtime/Core/     DDGIVolume, DDGIRaytracingManager, DDGIProbeUpdater,
│   │                     DDGIAtlasManager, DDGIResourceProvider ...
│   ├── Runtime/Shaders/  DDGILightingCombined.compute, DDGIMonteCarloIntegration.compute,
│   │                     DDGISampling.hlsl, DDGIRaytracing/ ...
│   └── Editor/
│
├── SSRT3/                GTAO 半球切片采样参考实现（HDRP，只读）
└── com.unity.sponza-urp@ Sponza 测试场景资源
```

## 环境要求与配置

- Unity 2022.3+，Windows 平台
- 支持 DXR 1.0 的 GPU（不支持时自动回退至 ScreenSpace 模式）
- URP Deferred Rendering Path

### 配置步骤

1. 在 URP Renderer 上添加 `SSGIRendererFeature`，通过 Inspector 绑定所需的 Compute Shader 与纹理资源
2. 在场景 Volume 中添加 `SSGIVolumeComponent`，选择 GI 模式并调整参数
3. 如需启用 DDGI，额外添加 `DDGIApplyGIRendererFeature` 并在场景中放置 `DDGIVolume`
4. 将 GI 模式切换至 `MixedDDGI` 即可启用两套系统的联动

## 注意事项

- 项目修改了本地 URP 14.0.12 包中的 `DeferredLights.cs`，升级 Unity 版本时需手动合并相关改动
- 两套系统分别位于 `URPSSGI` 和 `DDGI` 命名空间下
- `SSGIRendererFeature` 中的 shader/texture 引用通过 `SerializeField` 序列化，切换场景后可能需要重新绑定
- 项目使用了较多 `multi_compile` 变体关键字，移植至其他项目时注意关键字冲突

## 许可
本项目包含的 Sponza 场景资源受以下许可约束：

Sponza 模型：CC BY 3.0 — © 2010 Frank Meinl, Crytek
NoEmotion HDRs 纹理：CC BY-ND 4.0 — © 2022 Peter Sanitra
Sponza 场景资源版权信息见 `Assets/com.unity.sponza-urp@5665fb87d0/copyright.txt`。
