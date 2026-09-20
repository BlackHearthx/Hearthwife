using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Idol = vanilla ward visual + chest in front. Placement grounded like the ward.
    /// Ward prefab name is guard_stone (not piece_guardstone).
    /// </summary>
    internal static class WifeIdol
    {
        internal const string PrefabName = "Hearthwife_Idol";
        private const string VisualRoot = "Hearthwife_IdolVisual_v16";
        private static readonly string[] LegacyRoots =
        {
            "Hearthwife_IdolVisual",
            "Hearthwife_IdolVisual_v3",
            "Hearthwife_IdolVisual_v4",
            "Hearthwife_IdolVisual_v5",
            "Hearthwife_IdolVisual_v6",
            "Hearthwife_IdolVisual_v7",
            "Hearthwife_IdolVisual_v8",
            "Hearthwife_IdolVisual_v9",
            "Hearthwife_IdolVisual_v10",
            "Hearthwife_IdolVisual_v11",
            "Hearthwife_IdolVisual_v12",
            "Hearthwife_IdolVisual_v13",
            "Hearthwife_IdolVisual_v14",
            "Hearthwife_IdolVisual_v15"
        };

        private static readonly string[] WardPrefabNames =
        {
            "guard_stone",
            "piece_guardstone"
        };

        internal static void Register()
        {
            var existing = PrefabManager.Instance.GetPrefab(PrefabName);
            if (existing != null)
            {
                try
                {
                    BuildShrineVisual(existing);
                    ConfigureIdolPiece(existing);
                    EnsureWearSafe(existing);
                    if (existing.GetComponent<WifeHome>() == null)
                    {
                        existing.AddComponent<WifeHome>();
                    }

                    Jotunn.Logger.LogInfo("Hearthwife: Wife Idol visuals refreshed (Custom/Piece triplanar v16)");
                }
                catch (System.Exception ex)
                {
                    Jotunn.Logger.LogWarning("Hearthwife: idol refresh failed: " + ex.Message);
                }

                return;
            }

            var icon = BuildIcon();
            var config = new PieceConfig
            {
                Name = "$hearthwife_idol_name",
                Description = "$hearthwife_idol_desc",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Furniture,
                CraftingStation = CraftingStations.Workbench,
                AllowedInDungeons = false,
                Icon = icon,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 10, 0, true),
                    new RequirementConfig("Resin", 8, 0, true),
                    new RequirementConfig("FineWood", 4, 0, true)
                }
            };

            // Chest base = real storage. Ward is visual-only beside it.
            var piece = new CustomPiece(PrefabName, "piece_chest_wood", config);
            PieceManager.Instance.AddPiece(piece);
            var prefab = piece.PiecePrefab;
            if (prefab == null)
            {
                Jotunn.Logger.LogError("Hearthwife: failed to clone idol chest");
                return;
            }

            var container = prefab.GetComponent<Container>();
            if (container != null)
            {
                container.m_name = "$hearthwife_idol_name";
                container.m_width = 4;
                container.m_height = 3;
            }

            BuildShrineVisual(prefab);
            ConfigureIdolPiece(prefab);
            EnsureWearSafe(prefab);

            if (prefab.GetComponent<WifeHome>() == null)
            {
                prefab.AddComponent<WifeHome>();
            }

            var pieceComp = prefab.GetComponent<Piece>();
            if (pieceComp != null)
            {
                pieceComp.m_icon = icon;
                pieceComp.m_name = "$hearthwife_idol_name";
                pieceComp.m_description = "$hearthwife_idol_desc";
                pieceComp.m_enabled = true;
            }

            Jotunn.Logger.LogInfo("Hearthwife: Wife Idol registered (Furniture tab)");
        }

        /// <summary>
        /// Runtime instances: rebuild visual only when missing/legacy.
        /// Never DestroyImmediate colliders/WearNTear on live world objects.
        /// </summary>
        internal static void RefreshInstanceVisual(GameObject instance)
        {
            RefreshInstanceVisualIfNeeded(instance, force: true);
        }

        internal static void RefreshInstanceVisualIfNeeded(GameObject instance, bool force = false)
        {
            if (instance == null)
            {
                return;
            }

            try
            {
                var existing = instance.transform.Find(VisualRoot);
                if (!force && existing != null &&
                    existing.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    EnsureWearSafe(instance);
                    return;
                }

                BuildShrineVisual(instance);
                EnsureWearSafe(instance);
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning("Hearthwife: instance visual refresh failed: " + ex.Message);
            }
        }

        private static void BuildShrineVisual(GameObject prefab)
        {
            DestroyChild(prefab, VisualRoot);
            foreach (var legacy in LegacyRoots)
            {
                DestroyChild(prefab, legacy);
            }

            var root = new GameObject(VisualRoot);
            root.transform.SetParent(prefab.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            var t = root.transform;

            // Only the concept totem. Chest remains as Container on the prefab, not a second mesh.
            var custom = IdolMesh.Attach(t);
            if (custom == null)
            {
                Jotunn.Logger.LogWarning("Hearthwife: concept totem mesh failed to load");
            }
            else
            {
                PieceSetup.HideVanillaMeshes(prefab, VisualRoot);
            }

            foreach (var r in t.GetComponentsInChildren<Renderer>(false))
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }
        }

        /// <summary>WearNTear.UpdateBiome needs at least one live Renderer for bounds.</summary>
        private static void EnsureWearSafe(GameObject prefab)
        {
            var wear = prefab.GetComponent<WearNTear>();
            if (wear == null)
            {
                return;
            }

            wear.m_supports = true;
            wear.m_support = 100f;
            wear.m_noSupportWear = true;
            wear.m_noRoofWear = true;

            // Prefer real visual renderers — never leave a Standard-shader cube (magenta in Valheim).
            var oldProxy = prefab.transform.Find("Hearthwife_WearProxy");
            if (oldProxy != null)
            {
                Object.DestroyImmediate(oldProxy.gameObject);
            }

            var visual = prefab.transform.Find(VisualRoot);
            var hasVisual = false;
            if (visual != null)
            {
                foreach (var r in visual.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null)
                    {
                        continue;
                    }

                    r.enabled = true;
                    hasVisual = true;
                }
            }

            if (hasVisual)
            {
                return;
            }

            // Last resort: tiny cube with a cloned wood material (not Standard → no pink).
            var dummy = new GameObject("Hearthwife_WearProxy");
            dummy.transform.SetParent(prefab.transform, false);
            dummy.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            dummy.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            var filter = dummy.AddComponent<MeshFilter>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            filter.sharedMesh = cube.GetComponent<MeshFilter>().sharedMesh;
            var srcMat = cube.GetComponent<MeshRenderer>().sharedMaterial;
            Object.DestroyImmediate(cube);
            var mr = dummy.AddComponent<MeshRenderer>();
            var wood = IdolMesh.StealWoodMaterialPublic();
            mr.sharedMaterial = wood != null ? new Material(wood) : srcMat;
            mr.enabled = true;
            mr.forceRenderingOff = true;
        }

        private static void StripWardChrome(GameObject ward)
        {
            foreach (var tr in ward.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null || tr == ward.transform)
                {
                    continue;
                }

                var n = tr.name;
                if (n.IndexOf("PlayerBase", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("ForceField", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Enabled", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Disabled", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Connect", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Space", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Banner", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Flag", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    n.IndexOf("Cloth", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    tr.gameObject.SetActive(false);
                }
            }
        }

        private static void SnapFeetToParentGround(Transform t)
        {
            if (t == null || t.parent == null)
            {
                return;
            }

            var renderers = t.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                return;
            }

            var minY = float.MaxValue;
            var any = false;
            foreach (var r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy)
                {
                    continue;
                }

                any = true;
                minY = Mathf.Min(minY, r.bounds.min.y);
            }

            if (!any)
            {
                return;
            }

            var groundY = t.parent.position.y;
            t.position += new Vector3(0f, groundY - minY, 0f);
        }

        /// <summary>Collider + placement — prefab registration only (not live instances).</summary>
        private static void ConfigureIdolPiece(GameObject prefab)
        {
            var wardPrefab = ResolveWardPrefab();
            var wardPiece = wardPrefab != null ? wardPrefab.GetComponent<Piece>() : null;

            var piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                if (wardPiece != null)
                {
                    piece.m_groundPiece = wardPiece.m_groundPiece;
                    piece.m_groundOnly = wardPiece.m_groundOnly;
                    piece.m_allowAltGroundPlacement = true;
                    piece.m_clipGround = wardPiece.m_clipGround;
                    piece.m_clipEverything = false;
                    piece.m_notOnTiltingSurface = wardPiece.m_notOnTiltingSurface;
                    piece.m_notOnFloor = false;
                    piece.m_noInWater = true;
                    piece.m_canRotate = true;
                    piece.m_allowRotatedOverlap = true;
                    piece.m_extraPlacementDistance = Mathf.Max(6, wardPiece.m_extraPlacementDistance);
                    piece.m_cultivatedGroundOnly = false;
                    piece.m_onlyInBiome = Heightmap.Biome.None;
                }
                else
                {
                    piece.m_groundPiece = false;
                    piece.m_groundOnly = false;
                    piece.m_allowAltGroundPlacement = true;
                    piece.m_clipGround = true;
                    piece.m_clipEverything = false;
                    piece.m_notOnTiltingSurface = false;
                    piece.m_notOnFloor = false;
                    piece.m_noInWater = true;
                    piece.m_canRotate = true;
                    piece.m_allowRotatedOverlap = true;
                    piece.m_extraPlacementDistance = 8;
                    piece.m_cultivatedGroundOnly = false;
                    piece.m_onlyInBiome = Heightmap.Biome.None;
                }

                piece.m_canBeRemoved = true;
            }

            EnsureWearSafe(prefab);

            PieceSetup.ConfigureGroundPiece(
                prefab,
                new Vector3(0f, 0.14f, 0f),
                new Vector3(0.5f, 0.28f, 0.5f));
        }

        private static GameObject ResolveWardPrefab()
        {
            foreach (var name in WardPrefabNames)
            {
                try
                {
                    var p = PrefabManager.Instance?.GetPrefab(name);
                    if (p != null)
                    {
                        return p;
                    }
                }
                catch
                {
                }

                try
                {
                    var p = ZNetScene.instance?.GetPrefab(name);
                    if (p != null)
                    {
                        return p;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static void DestroyChild(GameObject prefab, string childName)
        {
            var old = prefab.transform.Find(childName);
            if (old == null)
            {
                return;
            }

            // Visual roots have no WearNTear — DestroyImmediate is safe and avoids duplicate names.
            Object.DestroyImmediate(old.gameObject);
        }

        private static Sprite BuildIcon()
        {
            const int size = 64;
            var tex = LoadIconTexture(size) ?? BuildFallbackIconTexture(size);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        private static Texture2D LoadIconTexture(int expectSize)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string resource = null;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("hearthwife_idol_icon.rgba", System.StringComparison.OrdinalIgnoreCase))
                    {
                        resource = name;
                        break;
                    }
                }

                if (resource == null)
                {
                    return null;
                }

                byte[] bytes;
                using (var stream = asm.GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var ms = new System.IO.MemoryStream())
                    {
                        stream.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                }

                if (bytes.Length < 16)
                {
                    return null;
                }

                var w = System.BitConverter.ToInt32(bytes, 0);
                var h = System.BitConverter.ToInt32(bytes, 4);
                if (w != expectSize || h != expectSize || bytes.Length < 8 + w * h * 4)
                {
                    return null;
                }

                var pixels = new byte[w * h * 4];
                System.Buffer.BlockCopy(bytes, 8, pixels, 0, pixels.Length);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.name = "Hearthwife_IdolIcon";
                tex.LoadRawTextureData(pixels);
                tex.Apply(false, false);
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D BuildFallbackIconTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var cx = (x - size * 0.5f) / size;
                    var cy = (y - size * 0.5f) / size;
                    var pillar = Mathf.Abs(cx) < 0.18f && cy > -0.35f && cy < 0.42f;
                    var baseStone = Mathf.Abs(cx) < 0.32f && cy > -0.42f && cy < -0.22f;
                    if (pillar || baseStone)
                    {
                        var shade = 0.28f + (cy + 0.4f) * 0.15f;
                        pixels[y * size + x] = new Color(shade * 1.1f, shade * 0.75f, shade * 0.45f, 1f);
                    }
                    else
                    {
                        pixels[y * size + x] = new Color(0f, 0f, 0f, 0f);
                    }
                }
            }

            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}
