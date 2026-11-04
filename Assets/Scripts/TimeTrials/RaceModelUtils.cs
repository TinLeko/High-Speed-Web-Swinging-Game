using UnityEngine;

public static class RaceModelUtils
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    public static void StripColliders(GameObject root)
    {
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            UnityEngine.Object.Destroy(collider);
    }

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
