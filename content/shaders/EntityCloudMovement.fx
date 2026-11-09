// Overlay = SpriteBatch-bound texture (this one scrolls/tiles)
texture Texture;
sampler TextureSampler = sampler_state
{
    Texture = <Texture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Wrap;
    AddressV = Wrap; // <-- wrap so scrolling tiles
};

// Mask = separate texture param (no tiling)
texture MaskTexture;
sampler MaskSampler = sampler_state
{
    Texture = <MaskTexture>;
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};

// Select sub-rect of the MASK atlas
float2 MaskUVScale = float2(1, 1);
float2 MaskUVOffset = float2(0, 0);

// Tint (white = no tint)
float4 OverlayColor = float4(1, 1, 1, 1);

// NEW: scroll params
// UV units per second: (+X right, -X left, +Y down, -Y up)
float2 ScrollSpeed = float2(0.2, 0.0);
float GlobalTime = 0.0; // set from C# each frame (seconds)

float4 PS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    // Scroll the overlay; frac keeps uv in 0..1, sampler Wrap repeats the texture
    float2 suv = frac(uv + ScrollSpeed * GlobalTime);

    float4 overlay = tex2D(TextureSampler, suv) * OverlayColor * color;

    // Sample only the selected mask frame
    float2 muv = uv * MaskUVScale + MaskUVOffset;
    float maskA = tex2D(MaskSampler, muv).a;

    float4 o;
    o.rgb = overlay.rgb * maskA;
    o.a = overlay.a * maskA;
    return o;
}

technique MaskImpose_Scroll
{
    pass P0
    {
        PixelShader = compile ps_3_0 PS();
    }
}
