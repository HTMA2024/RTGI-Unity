# DDGI LightProbe 项目概览与集成指南

## 1. 项目简介

本项目是一个基于 Unity 的 **DDGI（Dynamic Diffuse Global Illumination）动态漫反射全局光照** 系统，参考 NVIDIA RTXGI SDK 实现。核心功能是在场景中布置三维网格探针（Light Probe），通过 DXR 光线追踪实时更新探针数据，实现动态全局光照效果。

### 1.1 技术栈

- Unity（URP 渲染管线）
- DXR（DirectX Raytracing）
- HLSL Compute Shader
- C#

### 1.2 核心能力

- 实时光线追踪更新探针 Irradiance / Distance Atlas
- 支持动态光源和动态几何体
- Probe Relocation（探针重定位，避免嵌入几何体）
- Probe Classification（探针分类，标记无效探针）
- Probe Variability & Reduction（变异度计算与自适应更新频率）
- 防漏光穿墙机制（Surface Bias、Wrap Shading、Chebyshev Visibility、Weight Crushing）
- Sky-Only Ground Truth 验证框架
- 多阶段管线单元测试方案

### 1.3 GPU 管线概览（每帧）

```
1. Ray Tracing         → 填充 G-Buffer（Position, Normal, Albedo, Emission）
2. Probe Relocation    → 移动嵌入几何体的探针（可选）
3. Probe Classification→ 标记无效探针为 INACTIVE（可选）
4. Lighting Combined   → 直接光 + 间接光 + Radiance 合成（已合并为单 Pass）
5. Monte Carlo Integration → Irradiance / Distance 积分到 Atlas
6. Variability Reduction   → 计算全局变异度（自适应更新）
7. Border Update       → 八面体映射边框像素复制
8. Atlas Swap          → Ping-Pong 双缓冲交换（零拷贝）
```

---

## 2. 项目当前状态

### 2.1 已完成功能

| 功能模块 | 状态 | 说明 |
|---------|------|------|
| 基础 Ray Tracing + G-Buffer | ✅ 完成 | RayGen、ClosestHit、Miss、G-Buffer 写入 |
| Deferred Lighting | ✅ 完成 | 主方向光 + 点光源 + 聚光灯 + Shadow Map |
| Indirect Lighting | ✅ 完成 | 从上一帧 Atlas 采样间接光 |
| Radiance Composite | ✅ 完成 | BRDF 合成出射 Radiance |
| Monte Carlo Integration | ✅ 完成 | Irradiance / Distance 积分 + Hysteresis 时间滤波 |
| Border Update | ✅ 完成 | 八面体映射边框像素复制 |
| Probe Relocation | ✅ 完成 | 背面检测 + 三种 Case 偏移策略 + 椭球约束 |
| Probe Classification | ✅ 完成 | 背面比例 + 体素边界检测 |
| Probe Variability & Reduction | ✅ 完成 | 变异系数计算 + 多级归约 + AsyncGPUReadback |
| Sky-Only Validator | ✅ 完成 | 逐阶段 Ground Truth 验证 |
| Probe Visualization | ✅ 完成 | 多种可视化模式（Irradiance、Distance、Offset、State 等） |
| Apply GI Renderer Feature | ✅ 完成 | URP 集成，将 DDGI 间接光应用到场景 |

### 2.2 性能优化状态（Spec 追踪）

项目有一套 7 项性能优化的 Spec（位于 `.kiro/specs/ddgi-performance-optimization/`）：

| 优化项 | 状态 | 说明 |
|--------|------|------|
| 1. G-Buffer 带宽优化（R32→R16） | ✅ 完成 | PositionDistance 从 128bit 降为 64bit |
| 2. 三 Pass 合并为 LightingCombined | ✅ 完成 | 消除 2 次 G-Buffer 重复读取和 2 个中间缓冲区 |
| 3. Border Update 并行化 | ✅ 完成 | 从 per-probe 串行改为 per-pixel 并行 |
| 4. Monte Carlo 调度优化 | ✅ 完成 | 分离 Irradiance/Distance Dispatch |
| 5. Atlas Copy 消除（Ping-Pong） | ✅ 完成 | 双缓冲零拷贝替代全量复制 |
| 6. Relocation/Classification 频率优化 | ⏳ 待调研 | 需验证收敛特性后实施 |
| 7. CollectAdditionalLights GC 优化 | ⏳ 待调研 | 需 Profiler 确认 GC 开销后实施 |

### 2.3 已知待改进项

- 采样时未读取 Probe Relocation 偏移量（影响 Chebyshev 可见性精度）
- Relocation Case B 移动距离过于保守（`minSpacing * 0.01` vs RTXGI 的 `1.0`）
- Classification 未使用 Relocation 后的实际 Probe 位置
- 不支持 Volume 旋转和无限滚动（RTXGI 支持）

---

## 3. 架构概览

### 3.1 核心类职责

| 类 | 文件 | 职责 |
|----|------|------|
| `DDGIVolume` | Runtime/Core/DDGIVolume.cs | 探针网格管理、探针查询接口、Atlas 生命周期 |
| `DDGIVolumeDescriptor` | Runtime/Core/DDGIVolumeDescriptor.cs | 所有配置参数（间距、数量、Hysteresis、Relocation/Classification 开关等） |
| `DDGIProbe` | Runtime/Core/DDGIProbe.cs | 单个探针数据（位置、状态、Atlas UV、偏移量） |
| `DDGIAtlasManager` | Runtime/Core/DDGIAtlasManager.cs | Irradiance/Distance Atlas 纹理管理（Ping-Pong 双缓冲） |
| `DDGIRaytracingManager` | Runtime/Core/DDGIRaytracingManager.cs | 主调度器：加速结构、G-Buffer、所有 Compute Pass 调度 |
| `DDGIProbeUpdater` | Runtime/Core/DDGIProbeUpdater.cs | CPU 端更新循环，调用 RaytracingManager 各功能 |
| `DDGIProbeVisualizer` | Runtime/Core/DDGIProbeVisualizer.cs | 探针可视化（多种调试模式） |
| `DDGISkyOnlyValidator` | Runtime/Core/DDGISkyOnlyValidator.cs | Sky-Only 场景 Ground Truth 验证 |
| `DDGISetupHelper` | Runtime/Core/DDGISetupHelper.cs | 初始化辅助工具 |
| `DDGIApplyGIRendererFeature` | Runtime/Core/DDGIApplyGIRendererFeature.cs | URP Renderer Feature，将 DDGI 间接光应用到场景 |

### 3.2 数据流

```
DDGIVolume (配置 + 探针网格)
    ↓
DDGIProbeUpdater (每帧驱动更新)
    ↓
DDGIRaytracingManager (GPU 管线调度)
    ├── Ray Tracing → G-Buffer
    ├── Relocation → ProbeData.xyz
    ├── Classification → ProbeData.w
    ├── LightingCombined → RadianceBuffer
    ├── Monte Carlo → Atlas (Ping-Pong)
    ├── Variability Reduction → 全局变异度
    └── Border Update → Atlas 边框
    ↓
DDGIAtlasManager (Atlas 双缓冲管理)
    ↓
DDGIApplyGIRendererFeature (URP 集成，采样 Atlas 应用到场景)
```

---

## 4. 关键物理量与公式

| 物理量 | 符号 | 说明 |
|--------|------|------|
| Radiance | L (W/m²·sr) | 单位面积、单位立体角的辐射功率 |
| Irradiance | E (W/m²) | 单位面积接收的辐射功率 |
| Lambertian BRDF | f_r = albedo / π | 漫反射 BRDF |

核心渲染方程：`L_o = L_e + (albedo/π) × (E_direct + E_indirect)`

蒙特卡洛积分：`E(n) ≈ Σ[L_i(ω_i) × max(0, n·ω_i)] / Σ[max(0, n·ω_i)]`

---

## 5. 文件索引

### 5.1 Runtime/Core — C# 核心逻辑

| 文件 | 说明 |
|------|------|
| `DDGIVolume.cs` | 探针体积管理器（MonoBehaviour），管理探针网格、Atlas 生命周期、探针查询 |
| `DDGIVolumeDescriptor.cs` | 配置数据结构：空间布局、Hysteresis、Gamma、Relocation/Classification 参数、Variability 参数、G-Buffer 精度选项 |
| `DDGIProbe.cs` | 单个探针数据类：位置、状态（Active/Inactive/Sleeping）、Atlas UV、偏移量 |
| `DDGIAtlasManager.cs` | Atlas 纹理管理：Irradiance/Distance Atlas 的 Ping-Pong 双缓冲创建、交换、清除 |
| `DDGIRaytracingManager.cs` | 主调度器：加速结构管理、G-Buffer 创建、所有 Compute Pass 的参数绑定与 Dispatch |
| `DDGIProbeUpdater.cs` | CPU 端更新循环：驱动每帧的完整管线（RT → Relocation → Classification → Lighting → Integration → Border → Swap） |
| `DDGIProbeVisualizer.cs` | 探针可视化：支持 Irradiance、Distance、Offset、State、BackfaceRatio 等多种调试模式 |
| `DDGISkyOnlyValidator.cs` | Sky-Only Ground Truth 验证：逐阶段对比 GPU 输出与 CPU 解析结果 |
| `DDGISetupHelper.cs` | 初始化辅助工具 |
| `DDGIApplyGIRendererFeature.cs` | URP Renderer Feature：将 DDGI 间接光采样结果应用到场景渲染 |

### 5.2 Runtime/Shaders — GPU Shader

| 文件 | 说明 |
|------|------|
| `DDGIRaytracing/DDGIRayGen.raytrace` | 光线生成 Shader：Fibonacci 球面采样 + Halton 旋转，发射光线填充 G-Buffer |
| `DDGIRaytracing/DDGIClosestHit.hlsl` | 最近命中 Shader：收集表面属性（Position、Normal、Albedo、Emission）+ 背面检测 |
| `DDGIRaytracing/DDGIMiss.hlsl` | Miss Shader：天空盒采样 |
| `DDGIRaytracing/DDGIGBuffer.hlsl` | G-Buffer 数据结构定义、Fibonacci 球面方向函数 |
| `DDGILightingCombined.compute` | 合并后的光照 Pass：一次 G-Buffer 读取完成直接光 + 间接光 + Radiance 合成 |
| `DDGIDeferredLighting.compute` | 延迟光照（已被 LightingCombined 替代，保留备用） |
| `DDGIIndirectLighting.compute` | 间接光采样（已被 LightingCombined 替代，保留备用） |
| `DDGIRadianceComposite.compute` | Radiance 合成（已被 LightingCombined 替代，保留备用） |
| `DDGIMonteCarloIntegration.compute` | 蒙特卡洛积分：Radiance → Irradiance/Distance Atlas + Variability 计算 + Border Update |
| `DDGIProbeUpdate.compute` | 探针更新 Compute Shader |
| `DDGIProbeRelocation.compute` | 探针重定位：背面检测 + 三种 Case 偏移 + 椭球约束 |
| `DDGIProbeClassification.compute` | 探针分类：背面比例 + 体素边界检测 → Active/Inactive |
| `DDGIVariabilityReduction.compute` | 变异度归约：多级 Reduction 计算全局平均变异度 |
| `DDGIAtlasCopy.compute` | Atlas 复制（已被 Ping-Pong 双缓冲替代，保留备用） |
| `DDGISampling.hlsl` | DDGI 采样函数库：Surface Bias、Wrap Shading、Chebyshev Visibility、Weight Crushing |
| `DDGIApplyGI.shader` | 应用 GI 的渲染 Shader |
| `DDGIProbeVisualization.shader` | 探针可视化 Shader（多种调试模式） |
| `DDGIProbeVisualizationSimple.shader` | 简化版探针可视化 |
| `DDGIGTProbeVisualization.shader` | Ground Truth 探针可视化 |
| `DDGILit.shader` | DDGI 光照材质 Shader |
| `DDGIURPLit.shader` | URP 版 DDGI Lit Shader |
| `URPLit.shader` | 基础 URP Lit Shader |

### 5.3 Editor — 编辑器扩展

| 文件 | 说明 |
|------|------|
| `DDGIVolumeEditor.cs` | DDGIVolume Inspector：Volume 编辑模式、参数 UI、Gizmo 绘制 |
| `DDGIProbeUpdaterEditor.cs` | DDGIProbeUpdater Inspector：更新模式选择、RT 参数配置 |
| `DDGIProbeVisualizerEditor.cs` | DDGIProbeVisualizer Inspector：可视化模式选择 |
| `DDGISkyOnlyValidatorEditor.cs` | Sky-Only Validator Inspector：验证按钮、报告显示 |
| `DDGIEditorMenu.cs` | 编辑器菜单项 |

### 5.4 Docs — 设计文档

| 文件 | 说明 |
|------|------|
| `DDGI_LightProbe_技术规范.md` | 核心技术规范：数据格式、Atlas 布局、探针管理、更新流程、查询插值、模块化架构 |
| `DDGI_Raytracing_Update_Pipeline.md` | 光线追踪更新管线设计：6 阶段详细流程、物理量定义、数据流、Buffer 设计 |
| `DDGI_Probe_Relocation_Design.md` | 探针重定位设计：RTXGI 算法分析、三种 Case 偏移策略、椭球约束、可视化 |
| `DDGI_Probe_Classification_Design.md` | 探针分类设计：背面比例检测、体素边界检测、与 Relocation 的协调 |
| `DDGI_Probe_Variability_Reduction_Design.md` | 变异度与归约设计：Welford 增量方差、多级 Reduction、自适应更新策略 |
| `DDGI_SkyOnly_GroundTruth_Validation.md` | Sky-Only 验证方案：逐阶段 Ground Truth 对比、已知潜在问题清单 |
| `DDGI_Pipeline_UnitTest_Plan.md` | 管线单元测试方案：可控场景设计、各阶段 CPU Ground Truth、误差量化指标 |
| `RTXGI_DDGI_Adaptive_Reduction_Comparison.md` | RTXGI 对比分析：Variability 实现差异、Per-Texel Hysteresis 策略对比 |
| `RTXGI_DDGI_AntiLightLeak_Analysis.md` | 防漏光机制对比：Relocation/Classification/Sampling 三层防线的 RTXGI vs 本项目差异 |
| `DDGI 光照探针模块化集成设计.docx` | 模块化集成设计文档（Word 格式） |

### 5.5 .kiro/specs — 性能优化 Spec

| 文件 | 说明 |
|------|------|
| `ddgi-performance-optimization/requirements.md` | 7 项优化需求：G-Buffer 带宽、Pass 合并、Border 并行化、MC 调度、Ping-Pong、频率优化、GC 优化 |
| `ddgi-performance-optimization/design.md` | 优化设计：架构变更、技术方案、数据模型变更、正确性属性、测试策略 |
| `ddgi-performance-optimization/tasks.md` | 实施任务列表：11 个任务组，含检查点，优化项 1-5 已完成，6-7 待调研 |

### 5.6 其他

| 文件/目录 | 说明 |
|-----------|------|
| `Runtime/DDGI.Runtime.asmdef` | Runtime Assembly Definition |
| `Editor/DDGI.Editor.asmdef` | Editor Assembly Definition |
| `Materials/PathTracing_Standard.mat` | 路径追踪标准材质 |
| `Scenes/` | 场景目录（当前为空） |

---

## 6. 关键配置参数速查

| 参数 | 默认值 | 说明 |
|------|--------|------|
| probeSpacing | (2, 2, 2) | 探针间距（米） |
| probeCounts | (5, 3, 5) | 各轴探针数量 |
| hysteresis | 0.97 | 时间滤波系数 |
| irradianceGamma | 5.0 | Irradiance Gamma 编码 |
| irradianceThreshold | 0.0 | Per-texel 加速阈值（0=禁用） |
| viewBias | 0.2 | 视线偏移（防漏光） |
| enableProbeRelocation | true | 启用探针重定位 |
| probeBackfaceThreshold | 0.25 | 背面命中比例阈值 |
| enableProbeClassification | true | 启用探针分类 |
| enableProbeVariability | true | 启用变异度计算 |
| enableAdaptiveUpdate | true | 启用自适应更新 |
| useHighPrecisionGBuffer | false | G-Buffer 精度（false=R16, true=R32） |

---

## 7. 集成注意事项

- 本项目使用 `DDGI` 命名空间，所有核心类均在此命名空间下
- Assembly Definition 文件为 `DDGI.Runtime` 和 `DDGI.Editor`
- 依赖 Unity URP 渲染管线和 DXR 光线追踪支持
- Shader 使用 Unity Compute Shader（.compute）和 HLSL（.hlsl）
- 性能优化 Spec 中的优化项 6、7 标记为"需深入验证"，实施前需额外调研
- 防漏光机制中"采样时读取 Relocation 偏移"是已知的最大功能差距，建议优先修复
