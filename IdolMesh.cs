using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Blender mesh as-is (uniform scale) + vanilla woodwall color via atlas UV island.
    /// </summary>
    internal static class IdolMesh
    {
        private const float TargetHeight = 1.45f;
        private const float WoodTileMeters = 0.55f;

        internal static GameObject Attach(Transform parent)
        {
            var mesh = LoadEmbeddedMesh();
            if (mesh == null)
            {
                return null;
            }

            BakeFeet(mesh);

            var src = FindWoodRenderer();
            if (src == null || src.sharedMaterial == null)
            {
                Jotunn.Logger.LogWarning("Hearthwife: no vanilla wood renderer");
                return null;
            }

            var mat = MakePieceWoodMaterial(src.sharedMaterial);
            if (mat == null)
            {
                return null;
            }

            var go = new GameObject("Hearthwife_IdolMesh");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.transform.localPosition = Vector3.zero;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.enabled = true;
            go.SetActive(true);
            go.layer = parent.gameObject.layer;

            Jotunn.Logger.LogInfo(
                $"Hearthwife: totem wood shader={mat.shader?.name} triplanar={(mat.HasProperty("_TriplanarMap") ? mat.GetFloat("_TriplanarMap") : -1f)} src={src.name}");
            return go;
        }

        private static void BakeFeet(Mesh mesh)
        {
            var verts = mesh.vertices;
            if (verts == null || verts.Length == 0)
            {
                return;
            }

            var minY = verts[0].y;
            for (var i = 1; i < verts.Length; i++)
            {
                if (verts[i].y < minY)
                {
                    minY = verts[i].y;
                }
            }

            for (var i = 0; i < verts.Length; i++)
            {
                verts[i].y -= minY;
            }

            mesh.vertices = verts;
            mesh.RecalculateBounds();
        }

        private static Material MakePieceWoodMaterial(Material src)
        {
            var piece = Shader.Find("Custom/Piece");
            Material mat;
            if (piece != null)
            {
                mat = new Material(piece);
                try
                {
                    mat.CopyPropertiesFromMaterial(src);
                }
                catch
                {
                }
            }
            else
            {
                mat = new Material(src);
            }

            CopyTex(src, mat, "_MainTex", "_BumpMap", "_MetallicTex", "_EmissionMap", "_NoiseTex");
            if (src.HasProperty("_Color") && mat.HasProperty("_Color"))
            {
                mat.SetColor("_Color", src.GetColor("_Color"));
            }

            if (mat.HasProperty("_TriplanarMap"))
            {
                mat.SetFloat("_TriplanarMap", 1f);
            }

            if (mat.HasProperty("_TriplanarLocalPos"))
            {
                mat.SetFloat("_TriplanarLocalPos", 1f);
            }

            if (mat.HasProperty("_TriplanarScale"))
            {
                mat.SetFloat("_TriplanarScale", 0.35f);
            }

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", Color.black);
            }

            return mat;
        }

        private static void CopyTex(Material from, Material to, params string[] names)
        {
            if (from == null || to == null)
            {
                return;
            }

            foreach (var n in names)
            {
                if (!from.HasProperty(n) || !to.HasProperty(n))
                {
                    continue;
                }

                var tex = from.GetTexture(n);
                if (tex != null)
                {
                    to.SetTexture(n, tex);
                }

                to.SetTextureScale(n, from.GetTextureScale(n));
                to.SetTextureOffset(n, from.GetTextureOffset(n));
            }
        }

        private static Renderer FindWoodRenderer()
        {
            foreach (var name in WoodPrefabs)
            {
                GameObject prefab = null;
                try
                {
                    prefab = Jotunn.Managers.PrefabManager.Instance?.GetPrefab(name);
                }
                catch
                {
                }

                if (prefab == null)
                {
                    try
                    {
                        prefab = ZNetScene.instance?.GetPrefab(name);
                    }
                    catch
                    {
                    }
                }

                if (prefab == null)
                {
                    continue;
                }

                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r.sharedMaterial == null || r.sharedMaterial.shader == null)
                    {
                        continue;
                    }

                    var sn = r.sharedMaterial.shader.name ?? "";
                    if (sn.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    if (sn.IndexOf("Piece", StringComparison.OrdinalIgnoreCase) < 0 &&
                        sn.IndexOf("Static", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    Jotunn.Logger.LogInfo("Hearthwife: wood from " + name + " / " + r.name + " (" + sn + ")");
                    return r;
                }
            }

            return null;
        }

        private static void ApplyValheimWoodUvs(Mesh mesh)
        {
            var island = SampleWoodUvIsland();
            var verts = mesh.vertices;
            var uvs = new Vector2[verts.Length];
            var tile = Mathf.Max(WoodTileMeters, 0.05f);
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                var u = v.x / tile;
                var vv = v.y / tile;
                u = u - Mathf.Floor(u);
                vv = vv - Mathf.Floor(vv);
                uvs[i] = new Vector2(island.x + u * island.z, island.y + vv * island.w);
            }

            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvs);
        }

        private static Vector4 SampleWoodUvIsland()
        {
            foreach (var name in WoodPrefabs)
            {
                GameObject prefab = null;
                try
                {
                    prefab = Jotunn.Managers.PrefabManager.Instance?.GetPrefab(name);
                }
                catch
                {
                }

                if (prefab == null)
                {
                    continue;
                }

                var filter = prefab.GetComponentInChildren<MeshFilter>(true);
                var src = filter != null ? filter.sharedMesh : null;
                if (src == null || src.uv == null || src.uv.Length < 8)
                {
                    continue;
                }

                var us = new List<float>(src.uv.Length);
                var vs = new List<float>(src.uv.Length);
                foreach (var uv in src.uv)
                {
                    us.Add(uv.x);
                    vs.Add(uv.y);
                }

                us.Sort();
                vs.Sort();
                var u0 = Percentile(us, 0.15f);
                var u1 = Percentile(us, 0.85f);
                var v0 = Percentile(vs, 0.15f);
                var v1 = Percentile(vs, 0.85f);
                var side = Mathf.Min(u1 - u0, v1 - v0);
                if (side < 0.03f)
                {
                    side = 0.08f;
                }

                var cu = (u0 + u1) * 0.5f;
                var cv = (v0 + v1) * 0.5f;
                Jotunn.Logger.LogInfo($"Hearthwife: wood UV island from {name} {side:F3} @ ({cu:F3},{cv:F3})");
                return new Vector4(cu - side * 0.5f, cv - side * 0.5f, side, side);
            }

            return new Vector4(0.06f, 0.06f, 0.10f, 0.10f);
        }

        private static float Percentile(List<float> sorted, float p)
        {
            if (sorted.Count == 0)
            {
                return 0f;
            }

            var i = Mathf.Clamp(Mathf.RoundToInt((sorted.Count - 1) * p), 0, sorted.Count - 1);
            return sorted[i];
        }

        private static void ApplyAlbedo(Material mat, Texture2D albedo)
        {
            if (mat == null || albedo == null)
            {
                return;
            }

            mat.mainTexture = albedo;
            foreach (var prop in new[] { "_MainTex", "_Diffuse", "_BaseMap", "_Albedo", "_MainTexArray" })
            {
                if (mat.HasProperty(prop))
                {
                    try
                    {
                        mat.SetTexture(prop, albedo);
                    }
                    catch
                    {
                    }
                }
            }

            // Reasonable UV scale for front-projected 0..1 map.
            try
            {
                mat.mainTextureScale = Vector2.one;
                mat.mainTextureOffset = Vector2.zero;
            }
            catch
            {
            }
        }

        internal static Material StealWoodMaterialPublic()
        {
            var r = FindWoodRenderer();
            return r != null ? r.sharedMaterial : null;
        }

        private static readonly string[] WoodPrefabs =
        {
            "wood_pole",
            "wood_pole2",
            "wood_wall_log",
            "woodwall",
            "wood_wall",
            "guard_stone"
        };

        private static Material StealWoodMaterial()
        {
            foreach (var name in WoodPrefabs)
            {
                GameObject prefab = null;
                try
                {
                    prefab = Jotunn.Managers.PrefabManager.Instance?.GetPrefab(name);
                }
                catch
                {
                }

                if (prefab == null)
                {
                    try
                    {
                        prefab = ZNetScene.instance?.GetPrefab(name);
                    }
                    catch
                    {
                    }
                }

                var src = prefab != null ? prefab.GetComponentInChildren<Renderer>(true) : null;
                if (src != null && src.sharedMaterial != null && src.sharedMaterial.shader != null)
                {
                    // Skip broken / magenta-prone shaders.
                    var sn = src.sharedMaterial.shader.name ?? "";
                    if (sn.IndexOf("InternalError", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        sn.IndexOf("Hidden/InternalError", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }

                    Jotunn.Logger.LogInfo("Hearthwife: idol base mat from " + name + " (" + sn + ")");
                    return src.sharedMaterial;
                }
            }

            return null;
        }

        /// <summary>Map totem reference onto the front of the pillar (X/Y → UV).</summary>
        private static void ApplyFrontProjectionUvs(Mesh mesh)
        {
            var verts = mesh.vertices;
            var b = mesh.bounds;
            var min = b.min;
            var size = b.size;
            size.x = Mathf.Max(size.x, 0.001f);
            size.y = Mathf.Max(size.y, 0.001f);
            size.z = Mathf.Max(size.z, 0.001f);

            var uvs = new Vector2[verts.Length];
            for (var i = 0; i < verts.Length; i++)
            {
                var v = verts[i];
                // Front atlas of the reference image.
                var u = Mathf.Clamp01((v.x - min.x) / size.x);
                var vv = Mathf.Clamp01((v.y - min.y) / size.y);
                // Slight side darkening via U squash toward edges for depth.
                uvs[i] = new Vector2(u, vv);
            }

            mesh.SetUVs(0, uvs);
        }

        private static Texture2D LoadEmbeddedTexture()
        {
            var bytes = ReadEmbeddedBytes("hearthwife_totem_ref.rgba");
            if (bytes == null || bytes.Length < 16)
            {
                Jotunn.Logger.LogWarning("Hearthwife: totem rgba missing — procedural wood");
                return MakeProceduralWood();
            }

            // Header: int32 width, int32 height, then bottom-up RGBA8.
            var w = BitConverter.ToInt32(bytes, 0);
            var h = BitConverter.ToInt32(bytes, 4);
            var need = 8 + (long)w * h * 4;
            if (w < 8 || h < 8 || w > 4096 || h > 4096 || bytes.Length < need)
            {
                Jotunn.Logger.LogWarning($"Hearthwife: bad totem rgba header {w}x{h}");
                return MakeProceduralWood();
            }

            var pixels = new byte[w * h * 4];
            Buffer.BlockCopy(bytes, 8, pixels, 0, pixels.Length);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
            tex.name = "Hearthwife_TotemRef";
            tex.LoadRawTextureData(pixels);
            tex.Apply(false, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        private static Texture2D MakeProceduralWood()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
            for (var y = 0; y < n; y++)
            {
                for (var x = 0; x < n; x++)
                {
                    var grain = Mathf.PerlinNoise(x * 0.08f, y * 0.02f);
                    var c = Mathf.Lerp(0.22f, 0.42f, grain);
                    tex.SetPixel(x, y, new Color(c * 1.05f, c * 0.72f, c * 0.42f, 1f));
                }
            }

            tex.Apply();
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private static Mesh LoadEmbeddedMesh()
        {
            var text = ReadEmbeddedText("Hearthwife_Idol.obj");
            if (string.IsNullOrEmpty(text))
            {
                Jotunn.Logger.LogWarning("Hearthwife: embedded Hearthwife_Idol.obj missing");
                return null;
            }

            return ParseObj(text);
        }

        private static string ReadEmbeddedText(string ending)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using (var stream = asm.GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var reader = new StreamReader(stream))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }

            return null;
        }

        private static byte[] ReadEmbeddedBytes(string ending)
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith(ending, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using (var stream = asm.GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }

            return null;
        }

        private static Mesh ParseObj(string text)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var outPos = new List<Vector3>();
            var outNrm = new List<Vector3>();
            var outUv = new List<Vector2>();
            var tris = new List<int>();
            var map = new Dictionary<string, int>();
            var inv = CultureInfo.InvariantCulture;

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length < 3 || line[0] == '#')
                    {
                        continue;
                    }

                    if (line.StartsWith("v ", StringComparison.Ordinal))
                    {
                        var p = Split(line);
                        positions.Add(new Vector3(Parse(p, 1, inv), Parse(p, 2, inv), Parse(p, 3, inv)));
                    }
                    else if (line.StartsWith("vn ", StringComparison.Ordinal))
                    {
                        var p = Split(line);
                        normals.Add(new Vector3(Parse(p, 1, inv), Parse(p, 2, inv), Parse(p, 3, inv)));
                    }
                    else if (line.StartsWith("vt ", StringComparison.Ordinal))
                    {
                        var p = Split(line);
                        uvs.Add(new Vector2(Parse(p, 1, inv), Parse(p, 2, inv)));
                    }
                    else if (line.StartsWith("f ", StringComparison.Ordinal))
                    {
                        var p = Split(line);
                        var idx = new int[p.Length - 1];
                        for (var i = 1; i < p.Length; i++)
                        {
                            idx[i - 1] = Corner(p[i], positions, normals, uvs, outPos, outNrm, outUv, map);
                        }

                        for (var i = 1; i + 1 < idx.Length; i++)
                        {
                            tris.Add(idx[0]);
                            tris.Add(idx[i]);
                            tris.Add(idx[i + 1]);
                        }
                    }
                }
            }

            if (outPos.Count == 0 || tris.Count == 0)
            {
                return null;
            }

            var mesh = new Mesh { name = "Hearthwife_Idol" };
            if (outPos.Count > 65000)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            mesh.SetVertices(outPos);
            if (outNrm.Count == outPos.Count)
            {
                mesh.SetNormals(outNrm);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            mesh.SetUVs(0, outUv);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static int Corner(
            string token,
            List<Vector3> positions,
            List<Vector3> normals,
            List<Vector2> uvs,
            List<Vector3> outPos,
            List<Vector3> outNrm,
            List<Vector2> outUv,
            Dictionary<string, int> map)
        {
            if (map.TryGetValue(token, out var existing))
            {
                return existing;
            }

            var parts = token.Split('/');
            var vi = Index(parts, 0, positions.Count);
            var ti = Index(parts, 1, uvs.Count);
            var ni = Index(parts, 2, normals.Count);

            outPos.Add(positions[vi]);
            outUv.Add(ti >= 0 && ti < uvs.Count ? uvs[ti] : Vector2.zero);
            outNrm.Add(ni >= 0 && ni < normals.Count ? normals[ni] : Vector3.up);

            var id = outPos.Count - 1;
            map[token] = id;
            return id;
        }

        private static int Index(string[] parts, int slot, int count)
        {
            if (slot >= parts.Length || string.IsNullOrEmpty(parts[slot]))
            {
                return -1;
            }

            if (!int.TryParse(parts[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            {
                return -1;
            }

            if (raw < 0)
            {
                raw = count + raw + 1;
            }

            return raw - 1;
        }

        private static float Parse(string[] parts, int i, CultureInfo inv)
        {
            if (i >= parts.Length)
            {
                return 0f;
            }

            float.TryParse(parts[i], NumberStyles.Float, inv, out var v);
            return v;
        }

        private static string[] Split(string line)
        {
            return line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
