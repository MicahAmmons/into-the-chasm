// Your smoke texture comes in via SpriteBatch
sampler2D TextureSampler : register(s0);

// === knobs (set from C#) ===
float GlobalTime = 0.0; // seconds
float2 Frequency = float2(6.0, 4.0); // how many waves across X/Y
float2 Speed = float2(0.6, 0.45); // wave motion rates
float DistortAmount = 0.08; // 0..~0.2 (how wispy)
float Opacity = 1.0; // overall alpha multiplier (keeps your texture color)

// Cheap sine-wave UV warp = "billowing"
float4 MainPS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float t = GlobalTime;

    // Use Y to bend X and X to bend Y; opposite phases to avoid directional crawl
    float dx = sin(uv.y * Frequency.x + t * Speed.x) * 0.5;
    float dy = cos(uv.x * Frequency.y - t * Speed.y) * 0.5;

    float2 warpedUV = uv + float2(dx, dy) * DistortAmount;
    warpedUV = clamp(warpedUV, float2(0.01, 0.01), float2(0.99, 0.99)); // tiny inset

    // Sample; keep original RGB, scale alpha only
    float4 c = tex2D(TextureSampler, warpedUV) * color;
    c.a = saturate(c.a * Opacity);
    return c;
}

technique Technique1
{
    pass P0
    {
        PixelShader = compile ps_2_0 MainPS();
    }
}
