#ifndef DITHER_UTILS_INCLUDED
#define DITHER_UTILS_INCLUDED

// 4x4 Bayer 矩阵（归一化到 [0,1]，共16个值）
// 原始值：[ 0, 8, 2,10; 12, 4,14, 6; 3,11, 1, 9; 15, 7,13, 5]
// 除以16归一化
static const float BAYER_MATRIX_4x4[16] =
{
     0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
    12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
     3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
    15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
};

// 根据屏幕像素坐标（整数模4）返回 [0,1] 范围的 Bayer 阈值
// screenPos: 像素坐标（未归一化的整数屏幕像素位置）
float BayerDither4x4(float2 screenPos)
{
    // 使用 uint 避免负数取模及 D3D11 性能警告
    uint2 pos = uint2(abs(floor(screenPos))) % 4u;
    uint index = pos.y * 4u + pos.x;
    return BAYER_MATRIX_4x4[index];
}

// 将 NdotL 分为若干色阶，返回 [0,1] 的漫反射系数
// NdotL:      法线与光源方向点积，[-1, 1]
// threshold:  亮暗分界阈值，通常为 0.0
// smoothness: 边缘软化程度（很小的值模拟硬边卡通效果）
float CelShade(float NdotL, float threshold, float smoothness)
{
    // 将 NdotL 映射到 [0,1]
    float diffuse = saturate(NdotL);
    // 使用 smoothstep 在 threshold 附近生成软/硬边
    float halfSmooth = max(smoothness * 0.5, 0.0001);
    return smoothstep(threshold - halfSmooth, threshold + halfSmooth, diffuse);
}

// 在色阶边界处叠加 Bayer 抖动，制造像素风格过渡效果
// NdotL:     法线与光源方向点积，[-1, 1]
// screenPos: 像素屏幕坐标（用于 Bayer 查表）
// steps:     色阶数量（1~4）
float DitheredCelShade(float NdotL, float2 screenPos, float steps)
{
    float diffuse = saturate(NdotL);
    
    // 将 diffuse 量化到 [0, steps] 区间
    float steppedVal = diffuse * steps;
    float floorVal   = floor(steppedVal);
    float frac_      = frac(steppedVal);
    
    // Bayer 阈值
    float threshold = BayerDither4x4(screenPos);
    
    // 在色阶边界进行抖动：如果小数部分超过 Bayer 阈值则进入下一阶
    float dithered = (frac_ > threshold) ? (floorVal + 1.0) : floorVal;
    
    // 重新归一化回 [0,1]
    return saturate(dithered / steps);
}

#endif // DITHER_UTILS_INCLUDED
