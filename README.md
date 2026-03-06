[中文版本 (Chinese Version)](README_CN.md)

# URP Real-Time Global Illumination

A real-time global illumination system built on Unity URP 14.0.12, implementing two independent GI solutions on top of Deferred Rendering + DXR pipeline.

## Showcase

![overview-1](assets/github-repo-images/RTGI-Unity/overview-1.gif)
![overview-2](assets/github-repo-images/RTGI-Unity/overview-2.gif)

### Mode Comparison

| SSGI (Screen Space) | RTGI (Ray Traced) | MixedDDGI | RTAO (Ray Traced) |
|---|---|---|---|
| ![ssgi](assets/github-repo-images/RTGI-Unity/ssgi.jpg) | ![rtgi](assets/github-repo-images/RTGI-Unity/rtgi.jpg) | ![mixed-ddgi](assets/github-repo-images/RTGI-Unity/mixed-ddgi.jpg) | ![rtao](assets/github-repo-images/RTGI-Unity/rtao.jpg) |

### GI On/Off Comparison

| GI Off | GI On |
|---|---|
| ![gi-off](assets/github-repo-images/RTGI-Unity/gi-off.jpg) | ![gi-on](assets/github-repo-images/RTGI-Unity/gi-on.jpg) |

### DDGI Probe Visualization

![probe-viz](assets/github-repo-images/RTGI-Unity/probe-viz.gif)

### Debug Visualization

| IndirectDiffuse | HitDistance | SSGI Mask |
|---|---|---|
| ![debug-indirect](assets/github-repo-images/RTGI-Unity/debug-indirect.jpg) | ![debug-hitdist](assets/github-repo-images/RTGI-Unity/debug-hitdist.jpg) | ![debug-mask](assets/github-repo-images/RTGI-Unity/debug-mask.jpg) |

## System Architecture

### URPSSGI (Assets/URPSSGI/)

Four GI modes supported:

- **ScreenSpace** — Hi-Z Ray Marching, pure screen-space tracing
- **RayTraced** — DXR hardware ray tracing
- **Mixed** — SSGI first, falls back to RTGI for missed pixels; miss shader samples skybox
- **MixedDDGI** — Same hybrid strategy as Mixed, but miss shader samples DDGI irradiance atlas instead

### DDGILightProbe (Assets/DDGILightProbe/)

A dynamic diffuse probe system based on the NVIDIA RTXGI SDK. Probes are placed on a uniform 3D grid, with DXR rays dispatched each frame to update irradiance/distance atlases. Supports probe relocation, classification, variability-based adaptive update, and a full light leaking suppression pipeline (surface bias, wrap shading, Chebyshev visibility test, weight crushing).

### System Integration

The two systems can run independently or work together via MixedDDGI mode: they share the same RTAS (built once per frame) and the same Closest Hit Shader. DDGI exposes atlas resources to URPSSGI through the `DDGIResourceProvider` static interface. When DDGI is disabled, the system automatically falls back to standard Mixed mode.

## Rendering Pipeline

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

## Key Technical Details

### URPSSGI

- Mip levels packed into a single `RWTexture2D` (Mip Atlas) to work around URP's inability to bind mip chains as UAVs
- Spatial denoiser uses world-space disk sampling with adaptive kernel radius, combined with depth/normal/plane-distance trilateral bilateral weights
- In Mixed mode, both SSGI and RTGI are shaded through the GBuffer → Deferred Lighting path, eliminating tonal bias between ColorPyramid and Closest Hit Shader
- Temporal Filter inlines history buffer writes + Merge-On-Read strategy, saving 5 full-screen dispatches
- Sampling sequences based on Owen-scrambled Sobol + per-pixel ranking/scrambling (BND sequences)
- Full Camera Relative Rendering throughout
- 20 built-in debug visualization modes

### DDGILightProbe

- Direct lighting, indirect lighting, and radiance compositing compressed into a single compute pass (LightingCombined)
- Atlas uses ping-pong double buffering for zero-copy swaps
- Variability reduction via multi-level parallel reduction + AsyncGPUReadback drives adaptive update frequency
- Probe visualization supports irradiance / distance / relocation offset / classification state / backface ratio modes

## Directory Structure

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
├── SSRT3/                GTAO hemisphere slice sampling reference (HDRP, read-only)
└── com.unity.sponza-urp@ Sponza test scene assets
```

## Requirements & Setup

- Unity 2022.3+, Windows
- GPU with DXR 1.0 support (falls back to ScreenSpace mode when unavailable)
- URP Deferred Rendering Path

### Setup Steps

1. Add `SSGIRendererFeature` to the URP Renderer; bind required Compute Shaders and textures via Inspector
2. Add `SSGIVolumeComponent` to a scene Volume; select GI mode and adjust parameters
3. To enable DDGI, additionally add `DDGIApplyGIRendererFeature` and place a `DDGIVolume` in the scene
4. Switch GI mode to `MixedDDGI` to enable both systems working together

## Notes

- This project modifies `DeferredLights.cs` in the local URP 14.0.12 package; manual merge is required when upgrading Unity
- The two systems reside under the `URPSSGI` and `DDGI` namespaces respectively
- Shader/texture references in `SSGIRendererFeature` are serialized via `SerializeField`; rebinding may be needed after switching scenes
- The project uses many `multi_compile` variant keywords; watch for keyword conflicts when porting to other projects

## License

This project contains Sponza scene assets under the following licenses:
- Sponza model: CC BY 3.0 — © 2010 Frank Meinl, Crytek
- NoEmotion HDRs textures: CC BY-ND 4.0 — © 2022 Peter Sanitra

See `Assets/com.unity.sponza-urp@5665fb87d0/copyright.txt` for full copyright information.
