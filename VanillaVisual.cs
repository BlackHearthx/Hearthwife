using Jotunn.Managers;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Clone vanilla meshes as decoration only. Never leave LightLod / WearNTear / ZNetView
    /// on the clone — those NREs on placement ghost and world CreateObject.
    /// </summary>
    internal static class VanillaVisual
    {
        internal static GameObject Attach(
            Transform parent,
            string prefabName,
            Vector3 localPosition,
            Vector3 localEuler,
            Vector3 localScale)
        {
            return AttachAny(parent, localPosition, localEuler, localScale, prefabName);
        }

        internal static GameObject AttachAny(
            Transform parent,
            Vector3 localPosition,
            Vector3 localEuler,
            Vector3 localScale,
            params string[] prefabNames)
        {
            GameObject source = null;
            string used = null;
            foreach (var name in prefabNames)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                source = ResolvePrefab(name);
                if (source != null)
                {
                    used = name;
                    break;
                }
            }

            if (source == null)
            {
                Jotunn.Logger.LogWarning($"Hearthwife: missing prefab(s) [{string.Join(", ", prefabNames)}]");
                return null;
            }

            // Clone under an inactive holder so Awake (LightLod/WearNTear/ZNetView) never runs
            // before StripGameplay. LightLod.Awake NREs if Light was stripped first / mid-Awake.
            var holder = new GameObject("Hearthwife_VisClone_Temp");
            holder.SetActive(false);
            var go = Object.Instantiate(source, holder.transform, false);
            go.name = used + "_vis";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(localEuler);
            go.transform.localScale = localScale;
            StripGameplay(go);
            SetLayerRecursive(go, parent.gameObject.layer);
            Object.DestroyImmediate(holder);
            // Keep inactive until parent activates — avoids Awake on half-stripped leftovers.
            go.SetActive(true);
            return go;
        }

        private static GameObject ResolvePrefab(string name)
        {
            try
            {
                var fromJotunn = PrefabManager.Instance?.GetPrefab(name);
                if (fromJotunn != null)
                {
                    return fromJotunn;
                }
            }
            catch
            {
            }

            try
            {
                if (ZNetScene.instance != null)
                {
                    return ZNetScene.instance.GetPrefab(name);
                }
            }
            catch
            {
            }

            return null;
        }

        private static void StripGameplay(GameObject go)
        {
            // Order matters: kill LightLod BEFORE Light (LightLod.Awake needs Light).
            DestroyAllByName(go, "LightLod");
            DestroyAllByName(go, "GuidePoint");
            DestroyAllByName(go, "LightFlicker");
            DestroyAllByName(go, "LodGroup");
            DestroyAllByName(go, "LODGroup");

            DestroyAll<EffectArea>(go);
            DestroyAll<PrivateArea>(go);
            DestroyAll<CircleProjector>(go);
            DestroyAll<ZSyncTransform>(go);
            DestroyAll<ZSyncAnimation>(go);
            DestroyAll<ZNetView>(go);
            DestroyAll<Piece>(go);
            DestroyAll<WearNTear>(go);
            DestroyAll<Container>(go);
            DestroyAll<CraftingStation>(go);
            DestroyAll<Fireplace>(go);
            DestroyAll<Door>(go);
            DestroyAll<ItemDrop>(go);
            DestroyAll<Sign>(go);
            DestroyAll<Bed>(go);
            DestroyAll<Animator>(go);
            DestroyAll<Collider>(go);
            DestroyAll<Rigidbody>(go);
            DestroyAll<Character>(go);
            DestroyAll<BaseAI>(go);
            DestroyAll<Player>(go);
            DestroyAll<Tameable>(go);
            DestroyAll<AudioSource>(go);
            DestroyAll<ParticleSystem>(go);
            DestroyAll<Light>(go);

            // Catch any remaining MonoBehaviours that aren't render/transform.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                {
                    continue;
                }

                var n = mb.GetType().Name;
                if (n == "LightLod" || n == "GuidePoint" || n == "LightFlicker" ||
                    n == "PrivateArea" || n == "EffectArea" || n == "CircleProjector" ||
                    n == "WearNTear" || n == "ZNetView" || n == "ZSyncTransform" ||
                    n == "ZSyncAnimation" || n == "Piece" || n == "Container")
                {
                    Object.DestroyImmediate(mb);
                }
            }
        }

        private static void DestroyAllByName(GameObject go, string typeName)
        {
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb != null && mb.GetType().Name == typeName)
                {
                    Object.DestroyImmediate(mb);
                }
            }
        }

        private static void DestroyAll<T>(GameObject go) where T : Object
        {
            foreach (var c in go.GetComponentsInChildren<T>(true))
            {
                Object.DestroyImmediate(c);
            }
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursive(child.gameObject, layer);
            }
        }
    }
}
