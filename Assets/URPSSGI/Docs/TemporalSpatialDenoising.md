# URPSSGI 时空间降噪系统原理文档

## 1. 概述

URPSSGI 的降噪系统从 HDRP `HDDiffuseDenoiser` + `TemporalFilter` 移植而来，针对 URP 14 的渲染管线特性做了适配。系统目标是将每帧仅 1spp（每像素一条光线）的高噪声 GI 结果，通过时间域累积和空间域滤波，收敛为稳定、干净的间接光照。

整体管线为两遍（Pass 1 + 可选 Pass 2）级联结构：

```
原始 noisy GI (1spp)
  │
  ├─ Pass 1: TemporalFilter(HF) → SpatialDenoise(radius=R, jitter=开)
  │
  └─ Pass 2（可选）: TemporalFilter(LF) → SpatialDenoise(radius=R×0.5, jitter=关)
  │
  └─ 半分辨率时: BilateralUpsample → 全分辨率输出
```

- Pass 1 处理高频噪声（HF = High Frequency），使用独立的历史缓冲 `RTGIHistoryHF`
- Pass 2 处理残余低频噪声（LF = Low Frequency），使用独立的历史缓冲 `RTGIHistoryLF`，空间滤波半径减半
- 两遍共享同一套 temporal filter 和 spatial denoiser shader，仅参数不同

---

## 2. 历史缓冲管理（SSGIHistoryManager）

### 2.1 Double-Buffer 架构

每个相机维护独立的 `SSGIHistoryManager` 实例（以 `Camera.GetInstanceID()` 为 key），管理 5 种历史缓冲的 double-buffer：

| Buffer ID | 格式 | 用途 |
|-----------|------|------|
| HistoryHF (0) | RGBA16F | 高频历史（rgb=颜色, a=sampleCount） |
| HistoryLF (1) | RGBA16F | 低频历史（rgb=颜色, a=sampleCount） |
| HistoryDepth (2) | R32F | 历史深度（temporal filter 深度验证） |
| HistoryNormal (3) | RGBA16F | 历史法线（temporal filter 法线验证） |
| HistoryMotionVector (4) | RG16F | 历史 per-object motion（运动向量拒绝） |

每种 buffer 有 frame[0]（当前帧）和 frame[1]（前一帧）两份。每帧结束时调用 `SwapAndSetReferenceSize` 交换指针：

```
帧 N 结束时 swap:
  frame[0] ←→ frame[1]
帧 N+1:
  GetCurrentFrame() → 原 frame[1]（现在是写入目标）
  GetPreviousFrame() → 原 frame[0]（帧 N 写入的数据，现在是读取源）
```

使用原生 `RenderTexture` 而非 RTHandle，完全绕过 URP RTHandle 自动缩放机制，确保纹理尺寸精确匹配工作分辨率。

### 2.2 前一帧矩阵缓存

`SSGIHistoryManager` 缓存前一帧的 View 和 GpuProj 矩阵（而非直接缓存 VP 矩阵）。原因：

Camera Relative Rendering 中，VP = GpuProj × View × Translate(cameraPos)。若直接缓存 VP，下一帧使用时 Translate 分量是上一帧的相机位置，但 shader 中 `currentPosWS` 是相对于当前帧相机的，坐标空间不匹配会导致平移相机时拖影。

正确做法：缓存 View 和 GpuProj，下一帧用当前帧的 `translateToCamera` 重新构建 PrevVP：

```csharp
Matrix4x4 prevVPMatrix = PrevGpuProjMatrix * (PrevViewMatrix * translateToCamera);
```

### 2.3 曝光值追踪

缓存前一帧的线性曝光乘数 `PrevExposure`，用于 temporal filter 中历史颜色的重曝光（详见 §3.5）。

---

## 3. 时间域去噪（SSGITemporalFilter.compute — TemporalFilter kernel）

时间域去噪的核心思想：利用帧间相关性，将多帧的 noisy GI 结果累积为低噪声输出。每帧将当前帧的 1spp 结果与历史累积结果加权混合，等效于多帧采样的蒙特卡洛积分。

### 3.1 Camera Motion Vector 自计算

RTGI pass 在 `AfterRenderingOpaques`（event 300）执行，而 URP 的 `MotionVectorRenderPass` 在 event 401 执行。因此 `_CameraMotionVectorsTexture` 在 temporal filter 执行时尚未渲染，必须自行计算 camera motion vector。

算法：

```
1. 从工作分辨率 UV 构建 clip space 坐标 posCS
2. 用当前帧 InvVP 反投影到世界空间: currentPosWS = (InvVP × [posCS, depth, 1]).xyz / .w
3. 用前一帧 PrevVP 投影到前一帧屏幕空间: prevClip = PrevVP × [currentPosWS, 1]
4. 透视除法得到 prevNDC，转换为 prevUV
5. motionVector = currentUV - prevUV
```

对静态几何体完全精确。动态物体有微小误差（可接受，GI 去噪不需要逐物体精度的 motion vector，逐物体运动由独立的运动向量拒绝机制处理）。

### 3.2 历史有效性验证（ValidateHistory）

重投影后的历史像素不一定可信，需要三重验证：

#### 3.2.1 UV 边界验证

```hlsl
float uvValid = step(0.0, prevUV.x) * step(prevUV.x, 1.0)
              * step(0.0, prevUV.y) * step(prevUV.y, 1.0);
```

重投影后的 UV 超出 [0,1] 范围说明该像素在前一帧不可见（如相机旋转导致的新暴露区域），历史数据无效。

#### 3.2.2 世界空间位置验证（像素足迹感知）

```hlsl
float maxRadius = ComputeMaxReprojectionWorldRadius(currentPosWS, currentNormal);
float positionValid = step(length(historyPosWS - currentPosWS), maxRadius);
```

比较当前帧世界空间位置与历史帧世界空间位置的距离。阈值不是固定值，而是像素足迹感知的自适应半径：

```
parallelFootprint = pixelSpreadAngleTangent × distance
realFootprint = parallelFootprint / |dot(normal, viewDir)|
maxRadius = max(MAX_REPROJECTION_DISTANCE, realFootprint × MAX_PIXEL_TOLERANCE)
```

- `pixelSpreadAngleTangent`：单个像素对应的视角正切值，由 FOV 和分辨率决定
- 远处像素的足迹更大，允许更大的位置偏差
- 掠射角（normal 与 viewDir 接近垂直）时足迹被拉伸，阈值相应增大
- `MAX_REPROJECTION_DISTANCE = 0.1`：最小阈值，防止近处像素阈值过小
- `MAX_PIXEL_TOLERANCE = 4`：允许 4 个像素足迹的偏差

#### 3.2.3 法线验证

```hlsl
half3 historyNormal = DecodeHistoryNormal(prevCoord);
float normalValid = step(MAX_NORMAL_DIFFERENCE, dot(historyNormal, currentNormal));
```

法线点积低于 `MAX_NORMAL_DIFFERENCE = 0.65`（约 49°）时判定为不同表面，历史无效。这能检测到遮挡关系变化（如物体移动后暴露出不同朝向的表面）。

### 3.3 运动向量拒绝（延迟一帧 Per-Object Motion）

#### 3.3.1 问题背景

RTGI 在 event 300 执行时，URP 的 `_CameraMotionVectorsTexture` 尚未渲染。自计算的 camera motion vector 只包含相机运动，无法检测物体运动。如果场景中有动态物体，其历史 GI 数据可能来自完全不同的表面，直接累积会产生拖影。

#### 3.3.2 解决方案：延迟一帧的 Per-Object Motion

`SSGICopyMotionVectorsPass` 在 event 402 执行（URP MotionVectorPass 之后），计算并存储 per-object motion：

```
per-object motion = 完整 MV（URP 渲染） - camera-only MV（自计算）
```

关键设计：
- 使用 `inv(_NonJitteredVP)` 重建世界位置确保 round-trip 精确：`_NonJitteredVP × inv(_NonJitteredVP) × clip = clip`
- 静态物体的 `fullMV - cameraMV` 精确为零（使用完全相同的矩阵体系）
- 动态物体的差值包含纯物体运动分量

存储到历史缓冲后，下一帧的 temporal filter 读取该数据做运动拒绝。延迟一帧的误差可接受：物体运动通常帧间连续，上一帧有运动的区域当前帧大概率仍在运动。

#### 3.3.3 Double-Buffer 时序

```
帧 N (event 300): TemporalFilter 读取 GetPreviousFrame(HistoryMotionVector) ← 帧 N-1 写入的数据
帧 N (event 300 后): SwapAndSetReferenceSize 交换 buffer
帧 N (event 402): CopyMotionVectors 写入 GetPreviousFrame(HistoryMotionVector)
帧 N+1 (event 300): TemporalFilter 读取 GetPreviousFrame(HistoryMotionVector) ← 帧 N 写入的数据 ✓
```

写入 `GetPreviousFrame` 而非 `GetCurrentFrame` 的原因：swap 在 event 300 已执行，event 402 时 swap 后的 `GetPreviousFrame` 指向的 buffer，在下一帧 swap 前仍是 `GetPreviousFrame`，恰好被 TemporalFilter 读取。

#### 3.3.4 软拒绝策略

```hlsl
float objectMotionPixels = length(objectMotion) * screenScale;
float motionAmount = smoothstep(4.0, 16.0, objectMotionPixels);
historyValid *= 1.0 - motionAmount;
```

不完全丢弃历史（`historyValid *= 0` 会导致输出 = 单帧 noisy GI → 闪烁），而是用 smoothstep 平滑衰减：
- < 4 像素：不触发拒绝（微小运动或噪声）
- 4~16 像素：线性过渡
- > 16 像素：最大衰减（`historyValid` 接近 0）

### 3.4 曝光控制

```hlsl
float exposureRatio = _ExposureMultiplier * rcp(_PrevExposureMultiplier);
historyGI *= exposureRatio * exposureValid;
```

历史颜色在前一帧曝光下存储，需要重曝光到当前帧曝光空间。曝光比超过 2× 时（`max(ratio, 1/ratio) > 2.0`）判定为曝光突变（如进出隧道），直接丢弃历史（`sampleCount = 0`），避免明暗过渡时的鬼影。

### 3.5 自适应累积

HDRP 对齐的累积策略，核心是 `sampleCount` 驱动的两阶段权重：

```
sampleCount < 8:  accumulationFactor = sampleCount / (sampleCount + 1)
                  → 帧 1: 0/1=0（纯当前帧）
                  → 帧 2: 1/2=0.5
                  → 帧 3: 2/3=0.67
                  → ...逐步增加历史权重
sampleCount >= 8: accumulationFactor = 0.93（锁定 93% 历史）
```

`sampleCount` 上限为 8，达到后锁定在 0.93 的累积因子。这意味着稳定状态下每帧输出 = 7% 当前帧 + 93% 历史，等效约 14 帧的指数移动平均。

#### 3.5.1 无效路径处理

历史完全无效（UV 越界/深度法线不匹配）时：
- `sampleCount = 1`，`accumulationFactor = 0`
- 输出 = 纯当前帧 noisy GI

运动向量拒绝（仅 `historyValid` 被衰减，但 UV/深度/法线验证通过）时：
- `sampleCount = 4`，`accumulationFactor = 0.75`
- 保留 75% 历史权重，显著降低噪点但允许更快响应运动

```hlsl
float motionOnly = step(0.5, uvValid * positionValid * normalValid);
float invalidFactor = motionOnly * 3.0 * rcp(4.0);  // 0.75 或 0.0
float invalidCount = lerp(1.0, 4.0, motionOnly);     // 4 或 1
```

### 3.6 内联历史写入

Temporal filter 同时输出到 `_TemporalFilterOutputRW`（供后续 spatial denoise 读取）和 `_HistoryOutputRW`（历史缓冲），省掉一次全屏 CopyHistory dispatch。

### 3.7 sampleCount 存储设计

`sampleCount` 存储在历史 buffer 的 `.w` 通道（而非独立纹理）。优点：
- 颜色和计数始终同步，避免不同步导致的累积错误
- 减少一张纹理的显存开销和带宽
- 双线性采样历史时，sampleCount 也被插值，自然处理亚像素偏移

### 3.8 不使用 AABB Clamp 的原因

TAA 常用的 AABB clamp（将历史颜色 clamp 到当前帧邻域的颜色范围内）不适合 GI 去噪。原因：GI 每帧的噪声模式不同（随机采样方向不同），AABB clamp 会将历史强制拉到当前帧的噪声范围，破坏多帧累积的收敛效果。

---

## 4. 空间域去噪（SSGIDiffuseDenoiser.compute — BilateralFilterColor kernel）

空间域去噪的核心思想：利用空间相关性，在世界空间中对邻域像素做加权平均。与屏幕空间滤波不同，世界空间采样能自适应距离和表面朝向，近处强降噪、远处保细节。

### 4.1 世界空间圆盘采样

不在屏幕空间做固定 kernel 的卷积，而是在世界空间中沿表面切平面做圆盘采样：

```
1. 读取中心像素深度 → 重建世界空间位置 centerPos
2. 读取中心像素法线 → 构建局部坐标系 (tangent, bitangent, normal)
3. 计算自适应滤波半径 denoisingRadius
4. 16 次圆盘采样：
   a. 从预计算的低差异序列读取 2D 采样点
   b. 缩放到 denoisingRadius
   c. 沿 tangent/bitangent 偏移到世界空间位置
   d. VP 投影回屏幕空间，读取该位置的 GI 颜色
   e. 计算四重双边权重
   f. 加权累积
5. 输出加权平均
```

#### 4.1.1 预计算采样点分布

`GeneratePointDistribution` kernel 在首帧执行一次，预计算 64 个圆盘采样点（4 组 × 16 个），存入 `StructuredBuffer<float2>`。采样点使用 Owen-scrambled Sobol 低差异序列 + 立方根映射（`SampleDiskCubic`），确保圆盘上均匀分布。

启用 jitter 时，每帧从 4 组中轮换选择一组（`sampleOffset = frameIndex * 16`），帧间采样位置不同，temporal filter 累积后等效于 64 个采样点的覆盖。

#### 4.1.2 自适应滤波半径

```hlsl
float ComputeMaxDenoisingRadius(float3 positionRWS)
{
    float distance = length(positionRWS);
    return distance * _DenoiserFilterRadius * rcp(lerp(5.0, 50.0, saturate(distance * 0.002)));
}
```

HDRP 经验公式：
- 近处（< 5m）：`distance × filterRadius / 5`（较大半径，强降噪）
- 远处（> 500m）：`distance × filterRadius / 50`（较小半径，保留细节）
- 中间距离线性插值

再与像素足迹做 max，确保滤波半径不小于像素在世界空间的投影大小：

```hlsl
float finalRadius = max(denoisingRadius, realPixelFootPrint * PIXEL_RADIUS_TOLERANCE_THRESHOLD);
```

### 4.2 VP 投影优化

循环内需要将世界空间采样点投影到屏幕空间。朴素做法是每次 `mul(VP, float4(wsPos, 1.0))`，即 16 次 4×4 矩阵乘法。

优化：利用线性性预计算，循环外做 3 次矩阵乘法，循环内仅需 2 次 MAD：

```hlsl
// 循环外预计算
float4 centerClip    = mul(VP, float4(centerPos, 1.0));
float4 tangentClip   = mul(VP, float4(tangent, 0.0));
float4 bitangentClip = mul(VP, float4(bitangent, 0.0));

// 循环内（MAD 形式）
float4 hClip = centerClip + tangentClip * sample.x + bitangentClip * sample.y;
```

数学等价性：`VP × (center + tangent×s.x + bitangent×s.y, 1) = VP×(center,1) + VP×(tangent,0)×s.x + VP×(bitangent,0)×s.y`

### 4.3 四重双边权重

每个采样点的权重由四个因子相乘：

```
w = gaussian(r, sigma) × w_depth × w_normal × w_plane × w_luminance
```

#### 4.3.1 高斯衰减

```hlsl
float gaussian(float radius, float sigma) {
    float t = radius * rcp(sigma);
    return exp(-t * t);
}
```

`sigma = 0.9 × denoisingRadius`，距中心越远权重越低。

#### 4.3.2 深度权重

```hlsl
float depthWeight = max(0.0, 1.0 - abs(tapZ01 - centerZ01) * DEPTH_WEIGHT);
```

使用 `Linear01Depth` 的绝对差异，`DEPTH_WEIGHT = 1.0`。深度差异越大权重越低，防止跨深度不连续边界（如物体边缘）的颜色泄漏。

#### 4.3.3 法线权重

```hlsl
half nd = saturate(dot(tapNormal, centerNormal));
half normalWeight = nd * nd * nd * nd;  // dot⁴
```

法线点积的四次方，对法线差异非常敏感。法线不同的表面（如墙角的两面墙）几乎不会互相影响。

#### 4.3.4 平面权重

```hlsl
float3 dq = centerPos - tapPos;
float planeError = max(abs(dot(dq, tapNormal)), abs(dot(dq, centerNormal)));
float planeW = saturate(1.0 - 2.0 * planeError * rsqrt(distance2));
float planeWeight = planeW * planeW;
```

检测采样点是否在中心像素的切平面上。即使深度和法线相似，如果两点不在同一平面（如平行但偏移的两面墙），平面权重会将其降权。

#### 4.3.5 亮度权重（Firefly 抑制）

```hlsl
float luminanceWeight = rcp(1.0 + tapLuminance);
```

异常亮的采样点（如天空 fallback 泄漏、光源直射）被降权。`L=0` 时权重=1，`L→∞` 时权重→0。这是对 HDRP 原始算法的扩展，有效抑制 firefly 噪点。

### 4.4 背景像素处理

- 中心像素为背景（`depth == UNITY_RAW_FAR_CLIP_VALUE`）：直接输出黑色
- 采样点为背景：跳过（`continue`），避免天空的垃圾法线数据引入高频噪声
- 深度一致性检测：`abs(tapEyeDepth - hClip.w) > 0.1` 时跳过，防止投影到错误深度的采样点

### 4.5 半分辨率支持

空间去噪器支持半分辨率工作：
- `_SSGIScreenSize`：工作分辨率（半分辨率时为全分辨率的一半）
- `_FullScreenSize`：全分辨率（深度/法线纹理的坐标空间）
- `resScale = _FullScreenSize.xy * _SSGIScreenSize.zw`：工作分辨率到全分辨率的映射比例

GI 颜色在工作分辨率坐标读取，深度/法线在全分辨率坐标读取。

---

## 5. 双边上采样（SSGIBilateralUpsample.compute）

半分辨率模式下，降噪后的 GI 结果需要上采样到全分辨率。简单双线性插值会在深度不连续处（物体边缘）产生光晕，双边上采样通过深度权重避免这个问题。

### 5.1 算法

```
1. 读取全分辨率 2×2 邻域深度，取最近深度（reversed-Z 下取 max）
2. 计算半分辨率纹理上的双线性采样 UV
3. Gather 半分辨率 RGB 的 4 个邻域值
4. 计算双线性插值权重
5. 深度加权：w_i = bilinearWeight_i × k_i / (|closestDepth - lowDepth_i| + ε)
6. 加权平均输出
```

权重公式中 `k = (9, 3, 1, 3)` 为距离衰减系数，偏向左上角采样点（与 Gather 的采样顺序对应）。

### 5.2 深度权重的作用

在物体边缘，半分辨率的 4 个邻域像素可能跨越前景和背景。深度权重确保：
- 全分辨率像素在前景 → 深度接近前景的半分辨率像素权重高
- 全分辨率像素在背景 → 深度接近背景的半分辨率像素权重高

避免前景 GI 泄漏到背景（或反之），保持边缘锐利。

---

## 6. RTAO 专用降噪（BilateralFilterAO kernel）

RTAO（Ray Traced Ambient Occlusion）使用轻量级的屏幕空间双边滤波，与 GI 的世界空间圆盘采样不同：

- 3×3 屏幕空间邻域（9 tap）
- 标量 float 输入/输出（AO 是单通道）
- 仅使用深度权重 + 法线权重（无平面权重、无亮度权重）
- 无世界空间位置重建、无 VP 矩阵投影

设计更轻量的原因：AO 信号比 GI 颜色简单得多（单通道、低频），不需要复杂的世界空间采样。

---

## 7. 辅助 Kernel

### 7.1 CopyDepth / CopyNormals

将当前帧的深度和法线拷贝到历史缓冲，供下一帧 temporal filter 做历史验证。使用 `LOAD_TEXTURE2D` 以像素坐标读取，避免 `cmd.Blit` 受 URP RTHandle 缩放影响。

半分辨率时做工作分辨率到全分辨率的坐标映射：`fullResCoord = workCoord × resScale`。

### 7.2 CopyDepthDual / CopyNormalsDual

Mixed 模式优化：一次 dispatch 同时写入 SSGIHistoryManager 管理的历史缓冲和 RTGI 独立历史缓冲，省掉一次全屏 dispatch。

### 7.3 CopyMotionVectors

在 event 402 执行，计算 per-object motion 并存入历史缓冲（详见 §3.3）。

---

## 8. C# 调度流程（RTGIRenderPass）

### 8.1 PerformRTGIDenoise

降噪入口方法，编排两遍级联降噪：

```
Pass 1:
  DispatchRTGITemporalFilter(input=rawGI, history=HistoryHF, output=TemporalOutputTemp)
  DispatchRTGISpatialDenoise(input=TemporalOutputTemp, output=SpatialDenoiseTemp,
                              jitter=frameIndex&3, radius=R)

Pass 2（可选）:
  DispatchRTGITemporalFilter(input=SpatialDenoiseTemp, history=HistoryLF, output=TemporalOutputTemp)
  DispatchRTGISpatialDenoise(input=TemporalOutputTemp, output=SpatialDenoiseTemp,
                              jitter=-1, radius=R×0.5)
```

关键：两遍 temporal filter 共享同一套 PrevVP 矩阵。前一帧矩阵缓存在 `Execute` 方法中统一更新（而非在 `DispatchRTGITemporalFilter` 内部），避免第二遍读到已被覆盖的当前帧矩阵。

### 8.2 DispatchRTGITemporalFilter

绑定输入/输出纹理和参数，dispatch temporal filter kernel。关键细节：

- 首帧历史无效时绑定当前帧作为历史（避免读取未初始化数据）
- 历史深度/法线必须绑定工作分辨率的历史缓冲（不能绑定全分辨率的 `_CameraDepthTexture`，因为 shader 用工作分辨率坐标 `prevCoord` 去 `LOAD_TEXTURE2D`）
- 首帧 PrevVP 为单位矩阵，位置验证必然失败，sampleCount 重置为 1（正确行为）

### 8.3 DispatchRTGISpatialDenoise

绑定输入/输出纹理和参数，dispatch spatial denoiser kernel。首次调用时额外 dispatch `GeneratePointDistribution` kernel 初始化采样点缓冲。

---

## 9. 数据流总结

```
帧 N:

[event 300] RTGI DispatchRays → rawGI (1spp, noisy)
                │
                ├─ TemporalFilter(HF):
                │   读取: rawGI, HistoryHF[prev], HistoryDepth[prev],
                │          HistoryNormal[prev], HistoryMotionVector[prev]
                │   写入: TemporalOutputTemp, HistoryHF[prev](内联)
                │
                ├─ SpatialDenoise(Pass 1):
                │   读取: TemporalOutputTemp, Depth, Normal
                │   写入: SpatialDenoiseTemp
                │
                ├─ TemporalFilter(LF)（可选）:
                │   读取: SpatialDenoiseTemp, HistoryLF[prev], ...
                │   写入: TemporalOutputTemp, HistoryLF[prev](内联)
                │
                ├─ SpatialDenoise(Pass 2)（可选）:
                │   读取: TemporalOutputTemp, Depth, Normal
                │   写入: SpatialDenoiseTemp → 最终降噪结果
                │
                ├─ CopyDepth: Depth → HistoryDepth[current]
                ├─ CopyNormals: Normal → HistoryNormal[current]
                ├─ SwapAndSetReferenceSize: current ←→ previous
                │
[event 402] CopyMotionVectors:
                读取: _CameraMotionVectorsTexture, Depth
                写入: HistoryMotionVector[previous]（swap 后的 previous = 下一帧的 previous）
```

---

## 10. 关键设计决策总结

| 决策 | 原因 |
|------|------|
| 自计算 camera MV 而非等待 URP | RTGI 在 event 300，URP MV 在 event 401，时序不允许 |
| 延迟一帧的 per-object motion | event 402 才有完整 MV，当前帧 temporal filter 已执行完毕 |
| 软拒绝而非硬拒绝 | 硬拒绝导致单帧 noisy 输出 → 闪烁 |
| 世界空间圆盘采样而非屏幕空间 kernel | 自适应距离和表面朝向，近处强降噪远处保细节 |
| sampleCount 存 .w 通道 | 颜色和计数同步，减少纹理开销 |
| 不用 AABB clamp | GI 噪声模式帧间不同，clamp 破坏累积收敛 |
| 缓存 View+GpuProj 而非 VP | Camera Relative Rendering 帧间坐标空间一致性 |
| 原生 RenderTexture 而非 RTHandle | 绕过 URP RTHandle 自动缩放，尺寸精确匹配 |
| 两遍级联降噪 | Pass 1 去高频噪声，Pass 2 去残余低频噪声，各自独立历史 |
