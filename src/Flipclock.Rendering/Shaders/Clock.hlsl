// Flipclock scene shaders: physically based tiles, a shadow-receiving background, and a depth-only shadow pass.

static const float PI = 3.14159265;

cbuffer Frame : register(b0)
{
    row_major float4x4 ViewProj;
    row_major float4x4 LightViewProj;
    float4 LightDir;     // xyz: unit vector toward the light
    float4 LightColor;   // rgb: radiance, a: shadow strength on the background
    float4 CameraPos;
    float4 DigitColor;   // linear rgb
    float4 BgColor;      // linear rgb, w: dim amount
    float4 BgUv;         // xy: scale, zw: offset for fill-cropping the background image
    float4 Params;       // x: has background image, y: shadow-map texel size, z: ambient, w: unused
};

cbuffer Object : register(b1)
{
    row_major float4x4 World;
    row_major float4x4 NormalMatrix;
    float4 BaseColor;    // linear rgb
    float4 Material;     // x: roughness, y: metallic, z: front glyph slice, w: back glyph slice (-1 = none)
};

Texture2DArray Glyphs : register(t0);
Texture2D<float> ShadowMap : register(t1);
Texture2D BgImage : register(t2);
SamplerState LinearSampler : register(s0);
SamplerComparisonState ShadowSampler : register(s1);

struct VSIn
{
    float3 pos : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float face : TEXCOORD1;
};

struct PSIn
{
    float4 pos : SV_Position;
    float3 world : TEXCOORD0;
    float3 normal : TEXCOORD1;
    float2 uv : TEXCOORD2;
    float face : TEXCOORD3;
    float4 lightPos : TEXCOORD4;
};

PSIn VSMain(VSIn i)
{
    PSIn o;
    float4 w = mul(float4(i.pos, 1), World);
    o.pos = mul(w, ViewProj);
    o.world = w.xyz;
    o.normal = mul(float4(i.normal, 0), NormalMatrix).xyz;
    o.uv = i.uv;
    o.face = i.face;
    o.lightPos = mul(w, LightViewProj);
    return o;
}

float4 VSShadow(VSIn i) : SV_Position
{
    return mul(mul(float4(i.pos, 1), World), LightViewProj);
}

static const float2 Poisson[16] =
{
    float2(-0.94201624, -0.39906216), float2(0.94558609, -0.76890725),
    float2(-0.09418410, -0.92938870), float2(0.34495938, 0.29387760),
    float2(-0.91588581, 0.45771432), float2(-0.81544232, -0.87912464),
    float2(-0.38277543, 0.27676845), float2(0.97484398, 0.75648379),
    float2(0.44323325, -0.97511554), float2(0.53742981, -0.47373420),
    float2(-0.26496911, -0.41893023), float2(0.79197514, 0.19090188),
    float2(-0.24188840, 0.99706507), float2(-0.81409955, 0.91437590),
    float2(0.19984126, 0.78641367), float2(0.14383161, -0.14100790),
};

// Soft shadow: 1 = fully lit, 0 = fully shadowed.
float ShadowVisibility(float4 lightPos, float radiusTexels)
{
    float3 p = lightPos.xyz / lightPos.w;
    float2 uv = float2(p.x * 0.5 + 0.5, -p.y * 0.5 + 0.5);
    if (any(uv < 0) || any(uv > 1) || p.z > 1) return 1;
    float sum = 0;
    [unroll] for (int k = 0; k < 16; k++)
        sum += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + Poisson[k] * Params.y * radiusTexels, p.z - 0.0015);
    return sum / 16;
}

float3 Environment(float3 dir)
{
    // A soft studio: bright overhead, dim floor.
    float up = saturate(dir.y * 0.5 + 0.5);
    return lerp(float3(0.10, 0.10, 0.11), float3(0.95, 0.95, 1.0), smoothstep(0.35, 0.95, up));
}

float3 Shade(float3 albedo, float3 N, float3 V, float roughness, float metallic, float shadow)
{
    float3 L = LightDir.xyz;
    float3 H = normalize(L + V);
    float NdotL = saturate(dot(N, L));
    float NdotV = max(dot(N, V), 1e-4);
    float NdotH = saturate(dot(N, H));
    float VdotH = saturate(dot(V, H));

    float a = max(roughness * roughness, 0.002);
    float a2 = a * a;
    float d = NdotH * NdotH * (a2 - 1) + 1;
    float D = a2 / (PI * d * d);
    float k = (roughness + 1) * (roughness + 1) / 8;
    float G = (NdotV / (NdotV * (1 - k) + k)) * (NdotL / (NdotL * (1 - k) + k));
    float3 F0 = lerp(0.04.xxx, albedo, metallic);
    float3 F = F0 + (1 - F0) * pow(1 - VdotH, 5);
    float3 specular = D * G * F / (4 * NdotV * NdotL + 1e-4);
    float3 kd = (1 - F) * (1 - metallic);
    float3 direct = (kd * albedo / PI + specular) * LightColor.rgb * NdotL * shadow * PI;

    float hemi = N.y * 0.5 + 0.5;
    float3 ambient = albedo * kd * lerp(0.55, 1.0, hemi) * Params.z;
    float3 Fa = F0 + (1 - F0) * pow(1 - NdotV, 5) * (1 - roughness);
    float3 R = reflect(-V, N);
    float3 envSpecular = Fa * Environment(R) * lerp(0.05, 0.55, (1 - roughness) * (1 - roughness)) * lerp(0.6, 1.0, shadow);
    return direct + ambient + envSpecular;
}

float3 ToSrgb(float3 c)
{
    c = c / (1 + 0.12 * c);   // gentle highlight rolloff
    return pow(saturate(c), 1 / 2.2);
}

float4 PSMain(PSIn i) : SV_Target
{
    float3 N = normalize(i.normal);
    float3 V = normalize(CameraPos.xyz - i.world);
    float3 albedo = BaseColor.rgb;

    float slice = i.face < 0.5 ? Material.z : (i.face < 1.5 ? Material.w : -1);
    if (slice >= 0)
    {
        float coverage = Glyphs.Sample(LinearSampler, float3(i.uv, slice)).r;
        albedo = lerp(albedo, DigitColor.rgb, coverage);
    }

    float shadow = ShadowVisibility(i.lightPos, 1.5);
    return float4(ToSrgb(Shade(albedo, N, V, Material.x, Material.y, shadow)), 1);
}

float4 PSBackground(PSIn i) : SV_Target
{
    float3 color = Params.x > 0.5
        ? BgImage.Sample(LinearSampler, i.uv * BgUv.xy + BgUv.zw).rgb
        : BgColor.rgb;
    color *= 1 - BgColor.w;
    float visibility = ShadowVisibility(i.lightPos, 6);
    color *= lerp(1 - LightColor.a, 1, visibility);
    return float4(pow(saturate(color), 1 / 2.2), 1);
}
