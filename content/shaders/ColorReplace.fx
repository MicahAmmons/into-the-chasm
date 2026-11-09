sampler2D TextureSampler : register(s0);

float4 TargetColor; // set from C#
float Strength; // 0..1 blend amount set from C#

float4 MainPS(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    float4 texColor = tex2D(TextureSampler, texCoord);

    // Preserve shading via luminance
    float brightness = dot(texColor.rgb, float3(0.299, 0.587, 0.114));
    float3 recolored = TargetColor.rgb * brightness;

    // Blend ORIGINAL color -> RECOLORED color by Strength (no transparency change)
    float3 finalRgb = lerp(texColor.rgb, recolored, saturate(Strength));

    // Keep original texture alpha * vertex alpha (opaque look preserved)
    return float4(finalRgb, texColor.a * color.a);
}

technique Technique1
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 MainPS();
    }
}
