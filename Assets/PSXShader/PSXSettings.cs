using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public class PSXSettings : MonoBehaviour
{
    [Header("Virtual resolution")]
    [Min(16)] public int virtualHeight = 240;
    [Tooltip("Derive the width from the screen aspect (e.g. 427x240 on 16:9) so pixels stay square.")]
    public bool matchScreenAspect = true;
    [Min(16)] public int fixedWidth = 320;

    [Header("Color")]
    [Tooltip("32 levels per channel = PS1's 15-bit color.")]
    [Range(2, 256)] public int colorLevels = 32;
    [Range(0f, 1f)] public float ditherStrength = 1f;

    [Header("Post pass")]
    [Tooltip("Pixelate inside the post shader. Turn off if you use URP Render Scale instead.")]
    public bool pixelateInPost = true;

    static readonly int ParamsId = Shader.PropertyToID("_PSX_Params");
    static readonly int FlagsId  = Shader.PropertyToID("_PSX_PostFlags");

    void OnEnable()   => Apply();
    void OnValidate() => Apply();
    void Update()     => Apply();

    void OnDisable()
    {
        Shader.SetGlobalVector(ParamsId, Vector4.zero);
        Shader.SetGlobalVector(FlagsId, Vector4.zero);
    }

    void Apply()
    {
        float width = fixedWidth;
        if (matchScreenAspect && Screen.height > 0)
            width = Mathf.Round(virtualHeight * (float)Screen.width / Screen.height);

        Shader.SetGlobalVector(ParamsId, new Vector4(width, virtualHeight, colorLevels, ditherStrength));
        Shader.SetGlobalVector(FlagsId, new Vector4(pixelateInPost ? 1f : 0f, 0f, 0f, 0f));
    }
}
