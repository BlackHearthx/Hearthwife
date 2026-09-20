using UnityEngine;

namespace Hearthwife
{
    internal static class PieceSetup
    {
        private static readonly Vector3 FootCenter = new Vector3(0f, 0.2f, 0f);
        private static readonly Vector3 FootSize = new Vector3(0.9f, 0.4f, 0.9f);

        internal static void HideVanillaMeshes(GameObject prefab, string visualRoot)
        {
            var visual = prefab.transform.Find(visualRoot);
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                {
                    continue;
                }

                if (visual != null && (renderer.transform == visual || renderer.transform.IsChildOf(visual)))
                {
                    continue;
                }

                if (renderer.transform == prefab.transform)
                {
                    continue;
                }

                if (visual != null && visual.IsChildOf(renderer.transform))
                {
                    continue;
                }

                // Do NOT disable MeshRenderers — WearNTear caches them and UpdateBiome NREs
                // on destroyed/disabled refs in Unity 6. Hide by scale instead.
                var t = renderer.transform;
                if (t.localScale.sqrMagnitude > 0.0001f)
                {
                    t.localScale = Vector3.one * 0.001f;
                }
            }
        }

        /// <summary>
        /// Furniture-style placement: on ground OR on other pieces (floors, tables).
        /// m_groundPiece must stay false — true forces "foundation only on terrain".
        /// </summary>
        internal static void ConfigureGroundPiece(GameObject prefab, Vector3 solidCenter, Vector3 solidSize)
        {
            var piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                piece.m_groundPiece = false;
                piece.m_groundOnly = false;
                piece.m_allowAltGroundPlacement = true;
                piece.m_clipGround = true;
                piece.m_clipEverything = true;
                piece.m_notOnTiltingSurface = false;
                piece.m_notOnFloor = false;
                piece.m_noInWater = true;
                piece.m_canRotate = true;
                piece.m_allowRotatedOverlap = true;
                piece.m_extraPlacementDistance = 6;
                piece.m_canBeRemoved = true;
                piece.m_cultivatedGroundOnly = false;
                piece.m_onlyInBiome = Heightmap.Biome.None;
            }

            var wear = prefab.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.m_supports = true;
                wear.m_support = 100f;
                wear.m_noSupportWear = true;
            }

            foreach (var c in prefab.GetComponentsInChildren<Collider>(true))
            {
                Object.DestroyImmediate(c);
            }

            var foot = prefab.AddComponent<BoxCollider>();
            foot.center = FootCenter;
            foot.size = FootSize;
            foot.isTrigger = false;

            var solidify = prefab.GetComponent<PieceSolidify>() ?? prefab.AddComponent<PieceSolidify>();
            solidify.SolidCenter = solidCenter;
            solidify.SolidSize = solidSize;
        }
    }

    public class PieceSolidify : MonoBehaviour
    {
        public Vector3 SolidCenter;
        public Vector3 SolidSize = Vector3.one;

        private void Start()
        {
            var nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                return;
            }

            foreach (var box in GetComponents<BoxCollider>())
            {
                Object.Destroy(box);
            }

            var boxNew = gameObject.AddComponent<BoxCollider>();
            boxNew.center = SolidCenter;
            boxNew.size = SolidSize;
            boxNew.isTrigger = false;
        }
    }
}
