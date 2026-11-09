sampler2D TextureSampler : register(s0);

// Set from C#
float4 TargetColor; // RGBA 0..1
float NumColumns = 20.0; // how many vertical columns
float GlobalTime = 0.0; // seconds since start
float Duration = 2.0; // time to light/unlight all columns

// Simple smoothstep replacement for ps_2_0
float smoothstep2(float edge0, float edge1, float x)
{
    float t = saturate((x - edge0) / (edge1 - edge0));
    return t * t * (3.0 - 2.0 * t);
}

float4 MainPS(float4 color : COLOR0, float2 texCoord : TEXCOORD0) : COLOR0
{
    // Sample texture
    float4 texColor = tex2D(TextureSampler, texCoord);

    // Which column are we in? (0..NumColumns-1)
    float columnIndex = floor(texCoord.x * NumColumns);

    // Progress (0..1 lighting, 1..0 unlighting)
    float totalCycle = Duration * 2.0;
    float timeInCycle = fmod(GlobalTime, totalCycle);
    float lightProgress = timeInCycle < Duration
        ? (timeInCycle / Duration) // lighting up
        : (1.0 - ((timeInCycle - Duration) / Duration)); // unlighting

    // Column activation threshold (0..1 across columns)
    float columnThreshold = (columnIndex + 1.0) / NumColumns;

    // Smooth fade for this column
    float fade = smoothstep2(columnThreshold - (1.0 / NumColumns), columnThreshold, lightProgress);

    // Mix original texture with TargetColor
    float3 finalRgb = lerp(texColor.rgb, TargetColor.rgb, fade);
    return float4(finalRgb, texColor.a * color.a);
}

technique Technique1
{
    pass Pass1
    {
        PixelShader = compile ps_2_0 MainPS();
    }
}
