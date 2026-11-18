using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static partial class DvrBuild
{
    private const string ArtFolder = Root + "/ProjectionArt";

    private static string AssetPath(string name, string extension)
    {
        if (!AssetDatabase.IsValidFolder(ArtFolder))
            AssetDatabase.CreateFolder(Root, "ProjectionArt");
        return $"{ArtFolder}/{name}.{extension}";
    }

    private static Material Finish(string name, uint color, float metal, float smoothness, Texture texture = null)
    {
        var path = AssetPath(name, "mat");
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }

        material.color = Hex(color);
        material.mainTexture = texture;
        material.SetFloat("_Metallic", metal);
        material.SetFloat("_Glossiness", smoothness);
        if (texture != null && name.StartsWith("Print ", StringComparison.Ordinal))
        {
            material.EnableKeyword("_EMISSION");
            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", Color.gray * 0.27f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Transform Profile(Transform parent, string name, Vector3 center, Vector3 scale, Material material)
    {
        var shape = MeshAt(parent, name, center, BeveledProfile(), material);
        shape.localScale = scale;
        return shape;
    }

    private static Transform MeshAt(Transform parent, string name, Vector3 center, Mesh mesh, Material material)
    {
        var shape = Place(name, parent, center);
        shape.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = shape.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return shape;
    }

    private static Mesh BeveledProfile()
    {
        var path = AssetPath("Beveled profiles", "asset");
        var cached = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (cached != null) return cached;

        var vertices = new List<Vector3>();
        var uv = new List<Vector2>();
        var triangles = new List<int>();
        const int cornerSamples = 6;
        const int ringCount = 4;
        const int sides = 4 * cornerSamples;
        for (var ring = 0; ring < ringCount; ring++)
        {
            var cap = ring == 0 || ring == ringCount - 1;
            var radius = cap ? 0.055f : 0.090f;
            var extent = cap ? 0.465f : 0.5f;
            var z = (ring < 2 ? -1 : 1) * (cap ? 0.5f : 0.365f);
            for (var corner = 0; corner < 4; corner++)
            {
                var angle = corner * Mathf.PI / 2;
                var midpoint = angle + Mathf.PI / 4;
                var origin = new Vector2(Mathf.Sign(Mathf.Cos(midpoint)), Mathf.Sign(Mathf.Sin(midpoint))) * (extent - radius);
                for (var sample = 0; sample < cornerSamples; sample++)
                {
                    var a = angle + Mathf.PI / 2 * sample / (cornerSamples - 1);
                    var point = origin + radius * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vertices.Add(new Vector3(point.x, point.y, z));
                    uv.Add(point + Vector2.one * 0.5f);
                }
            }
        }

        for (var i = 0; i < sides; i++)
        {
            var next = (i + 1) % sides;
            for (var ring = 0; ring < ringCount - 1; ring++)
            {
                var a = ring * sides + i;
                var b = ring * sides + next;
                triangles.AddRange(new[] { a, b, a + sides, b, b + sides, a + sides });
            }
        }

        for (var i = 1; i < sides - 1; i++)
        {
            triangles.AddRange(new[] { 0, i + 1, i });
            var rear = (ringCount - 1) * sides;
            triangles.AddRange(new[] { rear, rear + i, rear + i + 1 });
        }

        var mesh = new Mesh { name = "Beveled profiles" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    private static Texture2D Grain() => DrawTexture("Walnut grain", 256, 128, (x, y) =>
    {
        var knots = Mathf.PerlinNoise(x * 3, y * 1.6f);
        var grain = Mathf.PerlinNoise(x * 3.6f + knots, y * 50 + knots * 4);
        var pores = Mathf.PerlinNoise(x * 24, y * 148);
        var darkLine = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(y * 188f + knots * 12f), 19f);
        var value = (0.70f + 0.20f * grain + 0.08f * pores - 0.15f * darkLine);
        return new Color(value, value * 0.91f, value * 0.80f);
    });

    private static Texture2D DiscArt(string id) => DrawTexture("Artwork " + id, 256, 256, (u, v) =>
    {
        var x = 2 * u - 1;
        var y = 2 * v - 1;
        var r = Mathf.Sqrt(x * x + y * y);
        var angle = Mathf.Atan2(y, x);
        var accent = id switch
        {
            "offline" => Hex(0xE6BF64),
            "feature" => Hex(0xF7B05F),
            "sports" => Hex(0xF9D561),
            "indian" => Hex(0xFFC5A1),
            "news" => Hex(0x71C8D1),
            _ => Hex(0xDDB2EF)
        };
        var primary = id switch
        {
            "offline" => Hex(0x507759),
            "feature" => Hex(0x8F3454),
            "sports" => Hex(0x214E58),
            "indian" => Hex(0xB15134),
            "news" => Hex(0x29568A),
            _ => Hex(0x694B7B)
        };
        var shadow = Color.Lerp(primary, Hex(0x101416), 0.75f);
        var color = Color.Lerp(shadow, primary, 0.5f + 0.45f * y);

        switch (id)
        {
            case "offline":
                var hill = -0.15f + 0.19f * Mathf.Sin(x * 5 + 1);
                color = y < hill ? shadow : color;
                if (Disk(x - 0.34f, y - 0.37f, 0.23f)) color = accent;
                if (Disk(x + 0.29f, y - 0.13f, 0.19f)
                    || Ellipse(x + 0.43f + 0.11f * y, y - 0.37f, 0.061f, 0.22f)
                    || Ellipse(x + 0.23f - 0.12f * y, y - 0.39f, 0.065f, 0.24f))
                    color = Paper;
                break;
            case "feature":
                if ((Mathf.FloorToInt((angle + Mathf.PI) * 9f) & 1) == 0) color = Color.Lerp(color, accent, 0.40f);
                if (Disk(x - 0.02f, y - 0.30f, 0.29f)) color = accent;
                if (Ellipse(x - 0.09f, y - 0.24f, 0.16f, 0.25f)
                    || Ellipse(x + 0.03f, y - 0.31f, 0.17f, 0.23f))
                    color = shadow;
                if (Disk(x - 0.245f, y - 0.18f, 0.066f)) color = accent;
                break;
            case "sports":
                var lane = Mathf.Sqrt((x - 0.65f) * (x - 0.65f) + (y - 0.45f) * (y - 0.45f));
                color = Color.Lerp(color, accent, Mathf.Repeat(lane * 13.0f, 1) < 0.12f ? 0.85f : 0.05f);
                if (Disk(x + 0.13f, y - 0.30f, 0.26f)) color = accent;
                if (Disk(x + 0.13f, y - 0.30f, 0.254f) && (Mathf.Abs(y - 0.30f) < 0.012f
                    || Mathf.Abs((x + 0.13f) + 0.9f * (y - 0.30f) * (y - 0.30f)) < 0.015f))
                    color = primary;
                break;
            case "indian":
                var lotus = 0.46f + 0.12f * Mathf.Cos(12 * angle);
                if (r < lotus) color = Color.Lerp(primary, accent, 0.72f);
                if (Mathf.Abs(r - lotus) < 0.009f || Mathf.Abs(r - 0.68f) < 0.011f) color = Paper;
                if (y < -0.17f + 0.13f * Mathf.Sin(x * 11)) color = shadow;
                break;
            case "news":
                if (Disk(x + 0.04f, y - 0.26f, 0.36f))
                {
                    var px = x + 0.04f;
                    var py = y - 0.26f;
                    var world = Mathf.PerlinNoise(px * 11f + 6, py * 9f + 4) > 0.57f;
                    var grid = Mathf.Repeat(py * 11f, 1) < 0.09f || Mathf.Repeat((px + 0.12f * py * py) * 15f, 1) < 0.10f;
                    color = world || grid ? accent : primary;
                }
                if (Mathf.Abs(y - (0.26f + x * 0.23f)) < 0.012f) color = Paper;
                break;
            default:
                var folder = Mathf.Abs(x) < 0.42f && Mathf.Abs(y - 0.23f) < 0.19f;
                var tab = x > -0.42f && x < -0.09f && y > 0.40f && y < 0.49f;
                if (folder || tab) color = accent;
                if (folder && (Mathf.Abs(x) < 0.32f && Mathf.Abs(y - 0.19f) < 0.09f))
                    color = primary;
                if (Mathf.Repeat((x - y) * 14f, 1) < 0.055f) color = Color.Lerp(color, Paper, 0.21f);
                break;
        }

        if (r > 0.84f) color = Color.Lerp(color, shadow, 0.75f);
        if (Mathf.Abs(r - 0.841f) < 0.008f || Mathf.Abs(r - 0.914f) < 0.008f
            || Mathf.Abs(r - 0.208f) < 0.008f) color = Color.Lerp(color, accent, 0.84f);
        if (r > 0.865f && r < 0.895f && Mathf.Repeat((angle + Mathf.PI) * 10, 1) < 0.12f)
            color = Color.Lerp(color, accent, 0.6f);
        return color * (0.96f + 0.07f * Mathf.PerlinNoise(u * 142, v * 129));
    });

    private static bool Disk(float x, float y, float r) => x * x + y * y < r * r;

    private static bool Ellipse(float x, float y, float rx, float ry) => Disk(x / rx, y / ry, 1f);

    private static Texture2D DrawTexture(string name, int width, int height, Func<float, float, Color> pixel)
    {
        var path = AssetPath(name, "asset");
        var cached = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (cached != null) return cached;

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = name };
        var colors = new Color32[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                colors[y * width + x] = pixel((x + 0.5f) / width, (y + 0.5f) / height);
        texture.SetPixels32(colors);
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Trilinear;
        AssetDatabase.CreateAsset(texture, path);
        return texture;
    }

    private static AudioClip MechanicalClick()
    {
        const int rate = 22050;
        var path = AssetPath("Tray latch", "wav");
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip != null) return clip;

        const int count = 2425;
        using (var writer = new System.IO.BinaryWriter(System.IO.File.Create(path)))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + count * 2);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(count * 2);
            var random = new System.Random(5253);
            for (var i = 0; i < count; i++)
            {
                var t = (float)i / rate;
                var strike = Mathf.Exp(-70 * t) * Mathf.Sin(2 * Mathf.PI * 385 * t);
                var catchTime = t - 0.026f;
                var latch = catchTime > 0 ? Mathf.Exp(-190 * catchTime) * (0.6f - (float)random.NextDouble()) : 0;
                writer.Write((short)((strike * 0.55f + latch * 0.54f) * short.MaxValue));
            }
        }

        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private static void AddLight(Transform parent, string name, LightType type, Vector3 center, Color color, float strength, float range)
    {
        var light = Place(name, parent, center).gameObject.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = strength;
        light.range = range;
        light.shadows = LightShadows.None;
    }
}

