#ifndef MASTER_TERRAIN_DECO_INSTANCING_HELPERS_INCLUDED
#define MASTER_TERRAIN_DECO_INSTANCING_HELPERS_INCLUDED

struct MasterDecorationSpawnResult
{
    int indexID;
    int localID;
    float3 position;
    float4 rotation;
    float scale;
    float temperature;
    float moisture;
};

#if !defined(SHADERGRAPH_PREVIEW)
StructuredBuffer<MasterDecorationSpawnResult> _OutputBuffer;
int _LocalDecoID;
int _MaxPerDeco;
#endif




static const float3 BiomeColdDry = float3(0.843f, 0.882f, 0.941f);
static const float3 BiomeColdMid = float3(0.647f, 0.843f, 0.686f);
static const float3 BiomeColdWet = float3(0.176f, 0.529f, 0.314f);
static const float3 BiomeNormDry = float3(0.961f, 0.843f, 0.431f);
static const float3 BiomeNormMid = float3(0.667f, 0.902f, 0.353f);
static const float3 BiomeNormWet = float3(0.216f, 0.647f, 0.314f);
static const float3 BiomeHotDry  = float3(1.000f, 0.686f, 0.353f);
static const float3 BiomeHotMid  = float3(0.863f, 0.784f, 0.275f);
static const float3 BiomeHotWet  = float3(0.137f, 0.588f, 0.196f);

void setup() {}

float3 RotateVectorByQuat(float3 v, float4 q)
{
    return v + 2.0 * cross(q.xyz, cross(q.xyz, v) + q.w * v);
}

float HashUINT(uint v)
{
    v = (v ^ 61) ^ (v >> 16);
    v = v + (v << 3);
    v = v ^ (v >> 4);
    v = v * 0x27d4eb2d;
    v = v ^ (v >> 15);
    return (float)v / 4294967296.0f;
}

void ProcessGPUInstances_float(
    float3 InVertexPos,
    float3 InNormal,
    float InstanceID,
    out float3 OutWorldPos,
    out float3 OutWorldNormal,
    out float3 OutShadowTint,
    out float3 OutBiomeColor,
    out float OutRandomVariation)
{
    #if defined(SHADERGRAPH_PREVIEW)
    OutWorldPos = InVertexPos;
    OutWorldNormal = InNormal;
    OutShadowTint = float3(0.12f, 0.22f, 0.1f);
    OutBiomeColor = float3(0.4f, 0.65f, 0.3f);
    OutRandomVariation = 0.5f;
    return;
    #else

    uint iid = (uint)InstanceID + (uint)_LocalDecoID * (uint)_MaxPerDeco;
    MasterDecorationSpawnResult data = _OutputBuffer[iid];

    float3 pos = data.position;
    float4 q = data.rotation;
    float s = data.scale;

    float3 scaled = InVertexPos * s;
    float3 rotated = RotateVectorByQuat(scaled, q);
    float3 worldPosRaw = rotated + pos;

    OutWorldPos = mul(UNITY_MATRIX_I_M, float4(worldPosRaw, 1.0)).xyz;

    float3 worldNormalRaw = normalize(RotateVectorByQuat(InNormal, q));
    OutWorldNormal = mul((float3x3)UNITY_MATRIX_I_M, worldNormalRaw);

    float temperature = data.temperature;
    float moisture = data.moisture;

    float3 coldZone, normZone, hotZone;
    if (moisture < 0.5f) {
        coldZone = lerp(BiomeColdDry, BiomeColdMid, smoothstep(0.0f, 1.0f, moisture * 2.0f));
        normZone = lerp(BiomeNormDry, BiomeNormMid, smoothstep(0.0f, 1.0f, moisture * 2.0f));
        hotZone  = lerp(BiomeHotDry, BiomeHotMid, smoothstep(0.0f, 1.0f, moisture * 2.0f));
    } else {
        coldZone = lerp(BiomeColdMid, BiomeColdWet, smoothstep(0.0f, 1.0f, (moisture - 0.5f) * 2.0f));
        normZone = lerp(BiomeNormMid, BiomeNormWet, smoothstep(0.0f, 1.0f, (moisture - 0.5f) * 2.0f));
        hotZone  = lerp(BiomeHotMid, BiomeHotWet, smoothstep(0.0f, 1.0f, (moisture - 0.5f) * 2.0f));
    }

    if (temperature < 0.5f)
        OutBiomeColor = lerp(coldZone, normZone, smoothstep(0.0f, 1.0f, temperature * 2.0f));
    else
        OutBiomeColor = lerp(normZone, hotZone, smoothstep(0.0f, 1.0f, (temperature - 0.5f) * 2.0f));

    OutShadowTint = saturate(OutBiomeColor * 0.35f + float3(0.01f, 0.01f, 0.02f));

    uint hashX = asuint(data.position.x);
    uint hashZ = asuint(data.position.z);
    uint stableSeed = (hashX * 73856093u) ^ (hashZ * 19349663u);
    OutRandomVariation = HashUINT(stableSeed);
    #endif
}

void WorldPosDitherDiscard_float(float LODFade, float3 WorldPos, float CustomFadeThreshold, out float KeepMask)
{
    #if defined(SHADERGRAPH_PREVIEW)
    KeepMask = 1.0f;
    return;
    #else
    const float bayer[16] = {
         1.0/17.0,  9.0/17.0,  3.0/17.0, 11.0/17.0,
        13.0/17.0,  5.0/17.0, 15.0/17.0,  7.0/17.0,
         4.0/17.0, 12.0/17.0,  2.0/17.0, 10.0/17.0,
        16.0/17.0,  8.0/17.0, 14.0/17.0,  6.0/17.0
    };

    float2 wp = WorldPos.xz * 100.0;
    int ix = ((int)floor(abs(wp.x))) & 3;
    int iz = ((int)floor(abs(wp.y))) & 3;
    int idx = ix * 4 + iz;
    float threshold = bayer[idx];

    float finalFade = saturate(LODFade * CustomFadeThreshold);
    KeepMask = step(threshold, finalFade);
    #endif
}
#endif
