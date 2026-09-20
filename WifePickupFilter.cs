using System;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Category bitmask for what she gathers (bushes + ground drops).
    /// Shared by Recolher — Pickable and ItemDrop.
    /// </summary>
    [Flags]
    internal enum PickupCategory
    {
        None = 0,
        Berries = 1 << 0,
        Mushrooms = 1 << 1,
        Herbs = 1 << 2,
        Fish = 1 << 3,
        Materials = 1 << 4,
        Other = 1 << 5,

        /// <summary>Homestead defaults — no trophies/weapons.</summary>
        DefaultMask = Berries | Mushrooms | Herbs | Fish | Materials
    }

    internal static class WifePickupFilter
    {
        internal static string LabelPt(PickupCategory cat)
        {
            switch (cat)
            {
                case PickupCategory.Berries:
                    return "Frutas";
                case PickupCategory.Mushrooms:
                    return "Cogumelos";
                case PickupCategory.Herbs:
                    return "Ervas";
                case PickupCategory.Fish:
                    return "Peixe no chão";
                case PickupCategory.Materials:
                    return "Materiais";
                case PickupCategory.Other:
                    return "Outros no chão";
                default:
                    return cat.ToString();
            }
        }

        internal static string LabelEn(PickupCategory cat)
        {
            switch (cat)
            {
                case PickupCategory.Berries:
                    return "Berries";
                case PickupCategory.Mushrooms:
                    return "Mushrooms";
                case PickupCategory.Herbs:
                    return "Herbs";
                case PickupCategory.Fish:
                    return "Stranded fish";
                case PickupCategory.Materials:
                    return "Materials";
                case PickupCategory.Other:
                    return "Other ground loot";
                default:
                    return cat.ToString();
            }
        }

        internal static PickupCategory Classify(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return PickupCategory.Other;
            }

            var n = prefabName;

            if (Contains(n, "Raspberry") || Contains(n, "Blueberr") ||
                Contains(n, "Cloudberry") || Contains(n, "Berry"))
            {
                return PickupCategory.Berries;
            }

            if (Contains(n, "Mushroom") || Contains(n, "JotunPuffs") ||
                Contains(n, "Magecap") || Contains(n, "SmokePuff"))
            {
                return PickupCategory.Mushrooms;
            }

            if (Contains(n, "Thistle") || Contains(n, "Dandelion"))
            {
                return PickupCategory.Herbs;
            }

            // Stranded fish on ground — not bait, not fishing rod.
            if (Contains(n, "Fish") && !Contains(n, "FishingBait") && !Contains(n, "FishingRod"))
            {
                return PickupCategory.Fish;
            }

            if (IsMaterialName(n))
            {
                return PickupCategory.Materials;
            }

            return PickupCategory.Other;
        }

        internal static bool IsAllowedName(string prefabName, PickupCategory mask)
        {
            if (mask == PickupCategory.None)
            {
                return false;
            }

            var cat = Classify(prefabName);
            return (mask & cat) != 0;
        }

        internal static bool IsAllowedPickable(Pickable pick, PickupCategory mask)
        {
            if (pick == null)
            {
                return false;
            }

            if (mask == PickupCategory.None)
            {
                return false;
            }

            // Match item drop OR bush object name (RaspberryBush / BlueberryBush / …).
            if (pick.m_itemPrefab != null && IsPlantCatAllowed(pick.m_itemPrefab.name, mask))
            {
                return true;
            }

            if (IsPlantCatAllowed(pick.name, mask))
            {
                return true;
            }

            return pick.gameObject != null && IsPlantCatAllowed(pick.gameObject.name, mask);
        }

        private static bool IsPlantCatAllowed(string name, PickupCategory mask)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            var cat = Classify(name);
            if (cat != PickupCategory.Berries &&
                cat != PickupCategory.Mushrooms &&
                cat != PickupCategory.Herbs)
            {
                return false;
            }

            return (mask & cat) != 0;
        }

        internal static bool IsAllowedDrop(ItemDrop drop, PickupCategory mask)
        {
            if (drop == null || drop.m_itemData == null)
            {
                return false;
            }

            var name = drop.m_itemData.m_dropPrefab != null
                ? drop.m_itemData.m_dropPrefab.name
                : drop.gameObject.name;
            return IsAllowedName(name, mask);
        }

        /// <summary>Wild plant gather names (legacy forage list).</summary>
        internal static bool IsPlantGatherName(string name)
        {
            var cat = Classify(name);
            return cat == PickupCategory.Berries ||
                   cat == PickupCategory.Mushrooms ||
                   cat == PickupCategory.Herbs;
        }

        private static bool IsMaterialName(string n)
        {
            return Contains(n, "Wood") ||
                   Contains(n, "Resin") ||
                   Contains(n, "Stone") ||
                   Contains(n, "Flint") ||
                   Contains(n, "Feather") ||
                   Contains(n, "BoneFragments") ||
                   Contains(n, "Bone") ||
                   Contains(n, "LeatherScraps") ||
                   Contains(n, "DeerHide") ||
                   Contains(n, "WolfPelt") ||
                   Contains(n, "LoxPelt") ||
                   Contains(n, "TrollHide") ||
                   Contains(n, "Guck") ||
                   Contains(n, "Coal") ||
                   Contains(n, "FineWood") ||
                   Contains(n, "RoundLog") ||
                   Contains(n, "ElderBark") ||
                   Contains(n, "YggdrasilWood") ||
                   Contains(n, "BlackMetalScrap") ||
                   Contains(n, "IronScrap") ||
                   Contains(n, "CopperOre") ||
                   Contains(n, "TinOre") ||
                   Contains(n, "SilverOre") ||
                   Contains(n, "Obsidian") ||
                   Contains(n, "Crystal") ||
                   Contains(n, "Chitin") ||
                   Contains(n, "WitheredBone") ||
                   Contains(n, "Entrails") ||
                   Contains(n, "RawMeat") ||
                   Contains(n, "NeckTail") ||
                   Contains(n, "SerpentMeat") ||
                   Contains(n, "Honey");
        }

        private static bool Contains(string hay, string needle)
        {
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
