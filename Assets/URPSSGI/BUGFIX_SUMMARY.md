# SSGI 渲染管线错误修复总结

## 修复日期
2026-02-13

## 问题描述
运行时出现多个 Compute Shader 相关错误：
1. 深度金字塔：MIP 级别错误
2. 颜色金字塔：UAV 标志未设置
3. SSGI Kernel：参数未设置导致 Kernel 无效
4. 去噪器：输出纹理未设置
5. 纹理拷贝：空纹理引用

## 修复内容

### 1. 金字塔纹理 MIP 级别问题
**文件**: `URPSSGI/Runtime/SSGIRenderPass.cs`

**问题**: `RenderTextureDescriptor` 创建时未指定 `mipCount`，导致纹理只有 1 个 MIP 级别

**修复**:
```csharp
// 在创建纹理前先计算 MIP 级数
m_DepthMipCount = DepthPyramidGenerator.GetMipCount(fullWidth, fullHeight);

// 为深度和颜色金字塔设置 mipCount
depthPyramidDesc.mipCount = m_DepthMipCount;
colorPyramidDesc.mipCount = m_DepthMipCount;
```

### 2. 颜色金字塔 UAV 标志问题
**文件**: `URPSSGI/Shaders/ColorPyramid.compute`

**问题**: 在非 `COPY_MIP_0` 模式下，`_Source` 被声明为 `RWTexture2D`，但绑定时使用的是只读 mip

**修复**:
```hlsl
// 将非 COPY_MIP_0 模式下的 _Source 改为只读
#if COPY_MIP_0
       Texture2D<float4>   _Source;
    RWTexture2D<float4>    _Mip0;
#else
       Texture2D<float4>    _Source;  // 改为只读
#endif
```

### 3. SSGI Compute Shader 参数缺失
**文件**: `URPSSGI/Runtime/SSGIRenderPass.cs`

**问题**: Compute Shader 无法访问通过 `SetGlobalTexture` 设置的全局纹理，需要显式绑定

**修复**:
- 在 `DispatchTrace` 中添加深度金字塔和法线纹理的显式绑定
- 在 `DispatchReproject` 中添加深度金字塔和运动向量纹理的显式绑定

```csharp
// DispatchTrace 中添加
cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
    SSGIShaderIDs._DepthPyramidTexture, m_DepthPyramid.nameID);
cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
    s_CameraNormalsTextureID,
    new RenderTargetIdentifier(s_CameraNormalsTextureID));

// DispatchReproject 中添加
cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
    SSGIShaderIDs._DepthPyramidTexture, m_DepthPyramid.nameID);
cmd.SetComputeTextureParam(m_SSGIComputeShader, kernel,
    SSGIShaderIDs._CameraMotionVectorsTexture,
    new RenderTargetIdentifier(SSGIShaderIDs._CameraMotionVectorsTexture));
```

### 4. 去噪器安全检查
**文件**: `URPSSGI/Runtime/SSGIRenderPass.cs`

**问题**: `PerformFullDenoise` 中未检查 `m_SpatialDenoiseTemp` 是否为 null

**修复**:
```csharp
// 添加 null 检查
if (!m_HasDenoiseShaders || m_SpatialDenoiseTemp == null)
    return;
```

## 测试建议

1. **基础渲染测试**
   - 启用 SSGI 功能
   - 检查 Console 是否还有错误
   - 验证深度和颜色金字塔是否正确生成

2. **分辨率测试**
   - 测试全分辨率模式
   - 测试半分辨率模式
   - 验证上采样是否正常工作

3. **去噪测试**
   - 启用去噪功能
   - 测试单遍去噪
   - 测试双遍去噪
   - 验证空间域滤波是否正常

4. **调试模式测试**
   - 测试各种调试可视化模式
   - 特别是 DenoiseComparison 模式

## 性能影响

所有修复均为正确性修复，不会引入额外的性能开销：
- MIP 级别设置：零开销（原本就应该设置）
- 纹理绑定：零开销（Compute Shader 必需的绑定）
- Null 检查：可忽略的分支开销

## 后续优化建议

1. 考虑在 Editor 中添加资源验证工具，检查所有必需的 Compute Shader 和纹理是否正确配置
2. 添加运行时诊断工具，显示当前 SSGI 状态和资源使用情况
3. 考虑添加更详细的错误日志，帮助快速定位问题
