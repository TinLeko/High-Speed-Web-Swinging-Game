using UnityEngine;

/// <summary>
/// mall helpers for working with hoop, arrow and marker models.
/// </summary>
public static class RaceModelUtils
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>
    /// Destroys every collider on an object and its children (including inactive ones), so decorative models cannot block the player.
    /// </summary>
    /// <param name="root">The model root.</param>
    public static void StripColliders(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.Destroy(collider);
    }

    /// <summary>
    /// Measures the opening radius of a model: the larger of the half-width and half-height of the combined mesh bounds, in the local space of
    /// <paramref name="root"/>. Expects a ring facing along the local Z axis.
    /// </summary>
    /// <param name="root">The transform to measure under.</param>
    /// <returns>The radius, or 1 if there are no meshes.</returns>
    public static float MeasureRadius(Transform root)
    {
        bool any = false;
        Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);

        foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (meshFilter.sharedMesh == null) continue;

            Bounds meshBounds = meshFilter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = meshBounds.center + Vector3.Scale(meshBounds.extents, sign);
                Vector3 local = root.InverseTransformPoint(meshFilter.transform.TransformPoint(corner));

                if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                else bounds.Encapsulate(local);
            }
        }

        return any ? Mathf.Max(bounds.extents.x, bounds.extents.y) : 1f;
    }

    /// <summary>
    /// Colours renderers using a <see cref="MaterialPropertyBlock"/> (materials are not duplicated). Sets <c>_BaseColor</c> and <c>_Color</c>,
    /// so it works with both URP/HDRP and built-in shaders, plus an optional custom property.
    /// </summary>
    /// <param name="renderers">The renderers to tint; null entries are skipped.</param>
    /// <param name="color">The colour.</param>
    /// <param name="extraProperty">Name of an additional shader colour property, or empty.</param>
    public static void Tint(Renderer[] renderers, Color color, string extraProperty = "")
    {
        var block = new MaterialPropertyBlock();
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null) continue;
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            if (!string.IsNullOrEmpty(extraProperty)) block.SetColor(Shader.PropertyToID(extraProperty), color);
            renderer.SetPropertyBlock(block);
        }
    }
}
