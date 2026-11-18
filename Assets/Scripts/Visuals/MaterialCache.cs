using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared runtime/editor cache for flat-colored materials and collider cleanup helpers.
/// Eliminates duplicate material allocations across GridManager, GameVisuals, and Arena builders.
/// </summary>
public static class MaterialCache
{
    private readonly struct MaterialKey : IEquatable<MaterialKey>
    {
        public readonly Color32 color;
        public readonly int smoothnessKey;

        public MaterialKey(Color32 color, int smoothnessKey)
        {
            this.color = color;
            this.smoothnessKey = smoothnessKey;
        }

        public bool Equals(MaterialKey other)
        {
            return color.r == other.color.r
                && color.g == other.color.g
                && color.b == other.color.b
                && color.a == other.color.a
                && smoothnessKey == other.smoothnessKey;
        }

        public override bool Equals(object obj)
        {
            return obj is MaterialKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = color.r;
                hash = (hash * 397) ^ color.g;
                hash = (hash * 397) ^ color.b;
                hash = (hash * 397) ^ color.a;
                hash = (hash * 397) ^ smoothnessKey;
                return hash;
            }
        }
    }

    private static Material s_SharedBaseMaterial;
    private static readonly Dictionary<MaterialKey, Material> s_CachedMaterials = new Dictionary<MaterialKey, Material>();

    public static Material GetColoredMaterial(Color color, float smoothness = 0.35f, Material templateMaterial = null)
    {
        int smoothnessKey = Mathf.RoundToInt(Mathf.Clamp01(smoothness) * 100f);
        MaterialKey key = new MaterialKey((Color32)color, smoothnessKey);

        if (s_CachedMaterials.TryGetValue(key, out Material existing) && existing != null)
        {
            return existing;
        }

        if (templateMaterial != null && s_SharedBaseMaterial == null)
        {
            s_SharedBaseMaterial = templateMaterial;
        }

        Material sourceMat = templateMaterial != null ? templateMaterial : s_SharedBaseMaterial;
        Material mat;

        if (sourceMat != null)
        {
            mat = new Material(sourceMat);
        }
        else
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default")
                         ?? Shader.Find("Standard");
            mat = new Material(shader);
            s_SharedBaseMaterial = mat;
            mat = new Material(s_SharedBaseMaterial);
        }

        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        if (mat.HasProperty("_Smoothness"))
        {
            mat.SetFloat("_Smoothness", smoothness);
        }

        s_CachedMaterials[key] = mat;
        return mat;
    }

    public static void DestroyCollider(GameObject obj)
    {
        if (obj == null) return;
        Collider col = obj.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(col);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(col);
            }
        }
    }

    public static void DestroySafe(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(obj);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
