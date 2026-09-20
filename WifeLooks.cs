using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Customizable female looks — dress / hair / color / skin saved on the idol ZDO.
    /// </summary>
    internal static class WifeLooks
    {
        internal static readonly string[] Dresses =
        {
            "ArmorDress1", "ArmorDress2", "ArmorDress3", "ArmorDress4",
            "ArmorDress5", "ArmorDress6", "ArmorDress7", "ArmorDress10",
            "ArmorTunic10", "ArmorRagsChest"
        };

        internal static readonly string[] Hairs =
        {
            "Hair1", "Hair2", "Hair3", "Hair4", "Hair5", "Hair6", "Hair7", "Hair8",
            "Hair9", "Hair10", "Hair11", "Hair12", "Hair13", "Hair14", "Hair15",
            "Hair16", "Hair17", "Hair18", "Hair19", "Hair20", "Hair21", "Hair22",
            "Hair30", "Hair1"
        };

        internal static readonly Vector3[] HairColors =
        {
            new Vector3(0.15f, 0.10f, 0.08f), // near black
            new Vector3(0.25f, 0.15f, 0.08f), // dark brown
            new Vector3(0.45f, 0.28f, 0.14f), // brown
            new Vector3(0.55f, 0.35f, 0.18f), // light brown
            new Vector3(0.65f, 0.42f, 0.22f), // auburn
            new Vector3(0.75f, 0.55f, 0.30f), // blonde
            new Vector3(0.85f, 0.70f, 0.45f), // light blonde
            new Vector3(0.55f, 0.15f, 0.12f), // red
            new Vector3(0.70f, 0.70f, 0.72f), // silver
            new Vector3(0.35f, 0.22f, 0.40f)  // dark purple tint
        };

        internal static readonly Vector3[] SkinColors =
        {
            new Vector3(1.00f, 0.85f, 0.72f),
            new Vector3(0.95f, 0.75f, 0.60f),
            new Vector3(0.85f, 0.65f, 0.50f),
            new Vector3(0.70f, 0.50f, 0.38f),
            new Vector3(0.55f, 0.38f, 0.28f),
            new Vector3(0.40f, 0.28f, 0.20f)
        };

        internal static int DressCount => Dresses.Length;
        internal static int HairCount => Hairs.Length;
        internal static int HairColorCount => HairColors.Length;
        internal static int SkinCount => SkinColors.Length;

        // Kept for older call sites / hover one-liners.
        internal static int Count => DressCount;

        internal static string DressName(int index)
        {
            var i = Mathf.Clamp(index, 0, DressCount - 1);
            return "$hearthwife_dress_" + i;
        }

        internal static string HairName(int index)
        {
            var i = Mathf.Clamp(index, 0, HairCount - 1);
            return "$hearthwife_hair_" + i;
        }

        /// <summary>Full looks refresh — clears hands (idle / unequip tool).</summary>
        internal static void Apply(VisEquipment vis, int dress, int hair, int hairColor, int skin)
        {
            ApplyCore(vis, dress, hair, hairColor, skin, clearRightHand: true);
        }

        /// <summary>
        /// Re-apply dress/hair/skin without clearing the right-hand tool (hammer / rod).
        /// Humanoid.EquipItem on an empty armor inventory otherwise leaves her nude.
        /// </summary>
        internal static void ReassertClothes(VisEquipment vis, int dress, int hair, int hairColor, int skin)
        {
            ApplyCore(vis, dress, hair, hairColor, skin, clearRightHand: false);
        }

        /// <summary>Legacy preset index → dress only.</summary>
        internal static void Apply(VisEquipment vis, int presetIndex)
        {
            Apply(vis, presetIndex, presetIndex % HairCount, presetIndex % HairColorCount, 0);
        }

        internal static string DisplayKey(int dressIndex) => DressName(dressIndex);

        private static void ApplyCore(
            VisEquipment vis, int dress, int hair, int hairColor, int skin, bool clearRightHand)
        {
            if (vis == null)
            {
                return;
            }

            dress = Mathf.Clamp(dress, 0, DressCount - 1);
            hair = Mathf.Clamp(hair, 0, HairCount - 1);
            hairColor = Mathf.Clamp(hairColor, 0, HairColorCount - 1);
            skin = Mathf.Clamp(skin, 0, SkinCount - 1);

            // Female body — must stick or she looks like a random male clone.
            vis.SetModel(1);
            if (vis.m_nview != null && vis.m_nview.IsValid())
            {
                try
                {
                    vis.m_nview.GetZDO().Set(ZDOVars.s_modelIndex, 1);
                }
                catch
                {
                    vis.m_nview.GetZDO().Set("ModelIndex", 1);
                }
            }

            vis.SetSkinColor(SkinColors[skin]);
            vis.SetBeardItem(0);
            vis.SetHairItem(Hash(Hairs[hair]));
            vis.SetHairColor(HairColors[hairColor]);

            var chest = Hash(Dresses[dress]);
            if (chest == 0)
            {
                chest = Hash("ArmorDress1");
            }

            vis.SetChestItem(chest);
            vis.SetLegItem(0);
            vis.SetHelmetItem(0);
            vis.SetShoulderItem(0, 0, 1);
            vis.SetUtilityItem(0);
            vis.SetLeftItem(0, 0, 1);
            vis.SetLeftBackItem(0, 0, 1);
            vis.SetRightBackItem(0, 1);

            if (clearRightHand)
            {
                vis.SetRightItem(0, 1);
            }

            vis.UpdateEquipmentVisuals();
        }

        private static int Hash(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return 0;
            }

            if (ObjectDB.instance != null)
            {
                var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
                if (prefab != null)
                {
                    return prefab.name.GetStableHashCode();
                }
            }

            return prefabName.GetStableHashCode();
        }
    }
}
