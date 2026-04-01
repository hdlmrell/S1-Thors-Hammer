using System.Collections;
using MelonLoader;
using UnityEngine;

namespace ThorHammer;

/// <summary>
/// Ground impact VFX for Thor flight landings.
/// Creates a soft circular shockwave ring that expands and fades.
/// </summary>
internal static class ExplosionHelper
{
    private static Texture2D _ringTexture;
    private static Material _ringMaterial;
    private static Texture2D _explosionTexture;
    private static Material _explosionMaterial;
    private static Texture2D _debrisTexture;
    private static Material _debrisMaterial;

    /// <summary>Plays ground impact VFX. slamRadiusWorld should match gameplay OverlapSphere radius so the ring reads at attack size.</summary>
    internal static void PlayGroundImpactVFX(Vector3 position, float landingSpeed, float effectMultiplier = 1f, float slamRadiusWorld = 8f)
    {
        float radiusVisual = Mathf.Clamp(slamRadiusWorld * 1.4f, 5f, 140f) * effectMultiplier;
        float debrisScale = Mathf.Clamp(slamRadiusWorld / 8f, 0.5f, 8f) * effectMultiplier;
        float speedBlend = Mathf.Clamp01((landingSpeed - 12f) / 88f);

        PlayShockwaveRing(position, radiusVisual, debrisScale + speedBlend * 0.35f);
        PlayProceduralExplosion(position, landingSpeed, radiusVisual, effectMultiplier);
        PlayDebris(position, Mathf.Max(debrisScale, 0.4f));
    }

    private static void PlayShockwaveRing(Vector3 position, float maxSize, float scale)
    {
        var go = new GameObject("ThorGroundSlamShockwave");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        EnsureRingAssets();
        if (_ringTexture != null && _ringMaterial != null)
        {
            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateQuadMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.material = _ringMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            MelonCoroutines.Start(AnimateShockwave(go, maxSize, scale));
        }
        else
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    private static void PlayProceduralExplosion(Vector3 position, float landingSpeed, float radiusVisual, float effectMultiplier)
    {
        float explosionScale = Mathf.Clamp(radiusVisual * 0.22f * effectMultiplier, 1.2f, 28f);
        float maxSize = Mathf.Lerp(explosionScale, explosionScale * 1.15f, Mathf.Clamp01((landingSpeed - 18f) / 82f));
        var go = new GameObject("ThorGroundSlamExplosion");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        EnsureExplosionAssets();
        if (_explosionTexture != null && _explosionMaterial != null)
        {
            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateQuadMesh();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.material = _explosionMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            MelonCoroutines.Start(AnimateExplosion(go, maxSize, explosionScale));
        }
        else
        {
            UnityEngine.Object.Destroy(go);
        }
    }

    private static void PlayDebris(Vector3 position, float scale)
    {
        int count = Mathf.Clamp(Mathf.RoundToInt(10 * scale), 6, 18);
        EnsureDebrisAssets();
        if (_debrisTexture != null && _debrisMaterial != null)
        {
            MelonCoroutines.Start(AnimateDebris(position, count, scale));
        }
    }

    private static void EnsureRingAssets()
    {
        if (_ringTexture != null) return;

        int res = 256;
        _ringTexture = new Texture2D(res, res);
        _ringTexture.wrapMode = TextureWrapMode.Clamp;
        _ringTexture.filterMode = FilterMode.Bilinear;

        float cx = res * 0.5f;
        float cy = res * 0.5f;
        float innerRadius = 0.35f * res;
        float ringWidth = 0.12f * res;
        float outerRadius = innerRadius + ringWidth;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = 0f;
                if (dist >= innerRadius && dist <= outerRadius)
                {
                    float t = (dist - innerRadius) / ringWidth;
                    float falloff = 1f - Mathf.Abs(t - 0.5f) * 2f;
                    falloff = Mathf.SmoothStep(0f, 1f, falloff);

                    float angle = Mathf.Atan2(dy, dx);
                    float noise = 0.85f + 0.15f * Mathf.PerlinNoise(angle * 3f + 100f, dist * 0.1f);
                    float edgeVar = 0.92f + 0.08f * Mathf.PerlinNoise(angle * 5f, 50f);

                    alpha = falloff * noise * edgeVar;
                    alpha = Mathf.Clamp01(alpha);
                }

                byte g = (byte)255;
                byte a = (byte)(alpha * 240);
                _ringTexture.SetPixel(x, y, new Color32(g, g, g, a));
            }
        }
        _ringTexture.Apply();

        _ringMaterial = new Material(Shader.Find("Sprites/Default"));
        _ringMaterial.mainTexture = _ringTexture;
        _ringMaterial.color = new Color(1f, 1f, 1f, 0.6f);
        _ringMaterial.SetInt("_ZWrite", 0);
        _ringMaterial.renderQueue = 3000;
    }

    private static void EnsureExplosionAssets()
    {
        if (_explosionTexture != null) return;

        int res = 128;
        _explosionTexture = new Texture2D(res, res);
        _explosionTexture.wrapMode = TextureWrapMode.Clamp;
        _explosionTexture.filterMode = FilterMode.Bilinear;

        float cx = res * 0.5f;
        float cy = res * 0.5f;
        float maxRadius = res * 0.48f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = x - cx;
                float dy = y - cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float t = dist / maxRadius;

                float alpha = 0f;
                if (t <= 1f)
                {
                    float falloff = 1f - t * t;
                    float angle = Mathf.Atan2(dy, dx);
                    float noise = 0.8f + 0.2f * Mathf.PerlinNoise(angle * 4f, dist * 0.05f);
                    alpha = falloff * noise;
                    alpha = Mathf.Clamp01(alpha);
                }

                byte g = (byte)(200 + (byte)(alpha * 55));
                byte a = (byte)(alpha * 200);
                _explosionTexture.SetPixel(x, y, new Color32(g, (byte)(g * 0.96f), (byte)(g * 0.9f), a));
            }
        }
        _explosionTexture.Apply();

        _explosionMaterial = new Material(Shader.Find("Sprites/Default"));
        _explosionMaterial.mainTexture = _explosionTexture;
        _explosionMaterial.color = new Color(1f, 1f, 1f, 0.7f);
        _explosionMaterial.SetInt("_ZWrite", 0);
        _explosionMaterial.renderQueue = 3001;
    }

    private static void EnsureDebrisAssets()
    {
        if (_debrisTexture != null) return;

        int res = 32;
        _debrisTexture = new Texture2D(res, res);
        _debrisTexture.wrapMode = TextureWrapMode.Clamp;
        _debrisTexture.filterMode = FilterMode.Bilinear;

        float cx = res * 0.5f;
        float cy = res * 0.5f;

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float dx = (x - cx) / cx;
                float dy = (y - cy) / cy;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);

                float alpha = 0f;
                if (dist < 1f)
                {
                    float noise = Mathf.PerlinNoise(x * 0.5f, y * 0.5f);
                    alpha = (1f - dist) * (0.3f + 0.7f * noise);
                    alpha = Mathf.Clamp01(alpha);
                }

                byte g = (byte)(115 + (byte)(alpha * 45));
                byte a = (byte)(alpha * 220);
                _debrisTexture.SetPixel(x, y, new Color32(g, (byte)(g * 0.92f), (byte)(g * 0.85f), a));
            }
        }
        _debrisTexture.Apply();

        _debrisMaterial = new Material(Shader.Find("Sprites/Default"));
        _debrisMaterial.mainTexture = _debrisTexture;
        _debrisMaterial.color = new Color(1f, 1f, 1f, 0.9f);
        _debrisMaterial.SetInt("_ZWrite", 0);
        _debrisMaterial.renderQueue = 3002;
    }

    private static Mesh CreateQuadMesh()
    {
        var m = new Mesh();
        m.vertices = new Vector3[] {
            new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
            new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0)
        };
        m.uv = new Vector2[] {
            new Vector2(0, 0), new Vector2(1, 0),
            new Vector2(0, 1), new Vector2(1, 1)
        };
        m.triangles = new int[] { 0, 2, 1, 1, 2, 3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private static IEnumerator AnimateShockwave(GameObject go, float maxSize, float scale)
    {
        float duration = 0.55f + 0.25f * scale;
        float elapsed = 0f;

        while (go != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float size = Mathf.Lerp(0.1f, maxSize, t);
            float alpha = 1f - t;

            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && r.material != null)
            {
                var c = r.material.color;
                r.material.color = new Color(c.r, c.g, c.b, 0.6f * alpha);
            }
            yield return null;
        }

        if (go != null)
            UnityEngine.Object.Destroy(go);
    }

    private static IEnumerator AnimateExplosion(GameObject go, float maxSize, float scale)
    {
        float duration = 0.5f + 0.2f * scale;
        float elapsed = 0f;

        while (go != null && elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float size = Mathf.Lerp(0.05f, maxSize, t);
            float alpha = 1f - t * t;

            go.transform.localScale = new Vector3(size, size, 1f);
            var r = go.GetComponent<MeshRenderer>();
            if (r != null && r.material != null)
            {
                var c = r.material.color;
                r.material.color = new Color(c.r, c.g, c.b, 0.7f * alpha);
            }
            yield return null;
        }

        if (go != null)
            UnityEngine.Object.Destroy(go);
    }

    private static IEnumerator AnimateDebris(Vector3 center, int count, float scale)
    {
        float baseSize = 0.22f * scale;
        var quads = new GameObject[count];
        var velocities = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            float angle = (float)i / count * 360f * Mathf.Deg2Rad + UnityEngine.Random.Range(-0.2f, 0.2f);
            float speed = 1.5f * scale * (0.7f + UnityEngine.Random.Range(0f, 0.6f));
            velocities[i] = new Vector3(Mathf.Cos(angle) * speed, speed * 0.6f, Mathf.Sin(angle) * speed);

            var go = new GameObject("ThorDebris");
            go.transform.position = center;
            go.transform.rotation = Quaternion.Euler(-90f, UnityEngine.Random.Range(0f, 360f), 0f);
            go.transform.localScale = Vector3.one * baseSize * (0.7f + UnityEngine.Random.Range(0f, 0.6f));

            var mf = go.AddComponent<MeshFilter>();
            mf.mesh = CreateQuadMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.material = _debrisMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            quads[i] = go;
        }

        float duration = 0.7f + 0.35f * scale;
        float elapsed = 0f;
        float gravity = -8f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float dt = Time.deltaTime;

            for (int i = 0; i < count; i++)
            {
                if (quads[i] != null)
                {
                    velocities[i].y += gravity * dt;
                    quads[i].transform.position += velocities[i] * dt;
                    float alpha = 1f - (elapsed / duration) * (elapsed / duration);
                    var r = quads[i].GetComponent<MeshRenderer>();
                    if (r != null && r.material != null)
                    {
                        var c = r.material.color;
                        r.material.color = new Color(c.r, c.g, c.b, 0.9f * alpha);
                    }
                }
            }
            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (quads[i] != null)
                UnityEngine.Object.Destroy(quads[i]);
        }
    }
}
