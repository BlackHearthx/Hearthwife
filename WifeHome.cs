using System.Collections.Generic;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Idol / totem. Ward circle + chest + menu.
    /// E = chest. Shift+E = menu. Crouch+E = recall.
    /// "Set bed" → look at a vanilla bed and press E to assign.
    /// </summary>
    public class WifeHome : MonoBehaviour, Hoverable, Interactable
    {
        public const string AppearKey = "Hearthwife_Appear";
        public const string DressKey = "Hearthwife_Dress";
        public const string HairKey = "Hearthwife_Hair";
        public const string HairColorKey = "Hearthwife_HairColor";
        public const string SkinKey = "Hearthwife_Skin";
        public const string RepairKey = "Hearthwife_Repair";
        public const string CollectKey = "Hearthwife_Collect";
        public const string FishKey = "Hearthwife_Fish";
        public const string CookKey = "Hearthwife_Cook";
        public const string FireKey = "Hearthwife_Fire";
        public const string FireWoodReserveKey = "Hearthwife_FireWoodReserve";
        public const string MeadKey = "Hearthwife_Mead";
        public const string SitKey = "Hearthwife_Sit";
        public const string NapKey = "Hearthwife_Nap";
        public const string SmeltKey = "Hearthwife_Smelt";
        public const string FarmKey = "Hearthwife_Farm";
        /// <summary>Legacy forage toggle — migrated into Collect (Recolher).</summary>
        public const string ForageKey = "Hearthwife_Forage";
        public const string PickupMaskKey = "Hearthwife_PickupMask";
        public const string GardenKey = "Hearthwife_Garden";
        public const string EatKey = "Hearthwife_Eat";
        public const string SitFireKey = "Hearthwife_SitFire";
        public const string ProtectKey = "Hearthwife_Protect";
        public const string AffectionKey = "Hearthwife_Affection";
        public const string MusicKey = "Hearthwife_Music";
        public const string RestedKey = "Hearthwife_Rested";
        public const string ComfortKey = "Hearthwife_Comfort";
        public const string MapPinKey = "Hearthwife_MapPin";
        public const string LifestyleKey = "Hearthwife_Lifestyle";
        public const string NameKey = "Hearthwife_Name";
        public const string NameIndexKey = "Hearthwife_NameIdx";
        public const string CookRecipeKey = "Hearthwife_CookRecipe";
        public const string HasBedKey = "Hearthwife_HasBed";
        public const string BedXKey = "Hearthwife_BedX";
        public const string BedYKey = "Hearthwife_BedY";
        public const string BedZKey = "Hearthwife_BedZ";
        public const string HasFishKey = "Hearthwife_HasFish";
        public const string FishXKey = "Hearthwife_FishX";
        public const string FishYKey = "Hearthwife_FishY";
        public const string FishZKey = "Hearthwife_FishZ";

        private enum MenuSlot
        {
            Dress = 0,
            Hair = 1,
            HairColor = 2,
            Skin = 3,
            Recall = 4,
            SetBed = 5,
            ToggleRepair = 6,
            ToggleCollect = 7,
            ToggleFish = 8
        }

        private const int MenuCount = 9;

        internal static WifeHome BedLinkHome;
        internal static float BedLinkUntil;
        internal static WifeHome FishLinkHome;
        internal static float FishLinkUntil;

        private ZNetView _nview;
        private Container _container;
        private WifeAgent _wife;
        private CircleProjector _areaMarker;
        private float _thinkAt;
        private float _spawnRetryAt;
        private int _spawnFails;
        private bool _started;
        private int _menuIndex;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _container = GetComponent<Container>();
        }

            private void Start()
            {
                _started = true;
                _spawnRetryAt = Time.time + 0.5f;
                EnsureAreaMarker();
                // Only rebuild if legacy/missing visual — never tear down a healthy idol mid-load
                // (destroys MeshRenderers WearNTear still caches → UpdateBiome NRE forever).
                WifeIdol.RefreshInstanceVisualIfNeeded(gameObject);
            }

        private void Update()
        {
            if (!_started)
            {
                return;
            }

            if (_nview == null)
            {
                _nview = GetComponent<ZNetView>();
            }

            UpdateAreaMarkerVisibility();
            TickFishLink();
            WifeLimits.TickMaintain();

            if (_wife == null && Time.time >= _spawnRetryAt)
            {
                if (_nview != null && _nview.IsValid())
                {
                    EnsureWife();
                }

                _spawnRetryAt = Time.time + 2f;
            }

            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (Time.time < _thinkAt)
            {
                return;
            }

            // Don't burn the think slot while Boot is still running — that left her frozen
            // for a full ThinkInterval (~6.5s) after load before the first real decision.
            if (_wife != null && !_wife.IsBooted)
            {
                _thinkAt = Time.time + 0.35f;
                return;
            }

            _thinkAt = Time.time + Mathf.Max(1f, PluginConfig.ThinkInterval.Value);
            _wife?.TickWork(this);
        }

        /// <summary>Called when Boot finishes — schedule an immediate idle/work think.</summary>
        internal void RequestThinkSoon()
        {
            _thinkAt = 0f;
        }

        private void OnDestroy()
        {
            if (BedLinkHome == this)
            {
                BedLinkHome = null;
            }

            if (FishLinkHome == this)
            {
                FishLinkHome = null;
            }

            if (_wife != null)
            {
                _wife.Despawn();
                _wife = null;
            }
        }

        internal Container Storage => _container;
        internal Vector3 HomePosition => transform.position;
        internal float Radius => Mathf.Max(4f, PluginConfig.HomeRadius.Value);

        /// <summary>Totem chest has no free slots (hover "cheio" + pause collect).</summary>
        internal bool IsStorageFull
        {
            get
            {
                var inv = Storage?.GetInventory();
                if (inv == null)
                {
                    return true;
                }

                try
                {
                    return inv.GetEmptySlots() <= 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal bool DoRepair => GetFlag(RepairKey, PluginConfig.EnableRepair.Value);
        /// <summary>Master Recolher (ex-Coletar + Forragear). Soft-migrates legacy ForageKey.</summary>
        internal bool DoCollect
        {
            get
            {
                EnsureGatherMigration();
                return GetFlag(CollectKey,
                    PluginConfig.EnableCollect.Value || PluginConfig.EnableForage.Value);
            }
        }

        internal bool DoFish => GetFlag(FishKey, PluginConfig.EnableFish.Value);
        internal bool DoCook => GetFlag(CookKey, PluginConfig.EnableCook.Value);
        internal bool DoFire => GetFlag(FireKey, PluginConfig.EnableFire.Value);
        internal bool DoMead => GetFlag(MeadKey, PluginConfig.EnableMead.Value);
        internal bool DoSit => GetFlag(SitKey, PluginConfig.EnableSit.Value);
        internal bool DoNap => GetFlag(NapKey, PluginConfig.EnableNap.Value);
        internal bool DoSmelt => GetFlag(SmeltKey, PluginConfig.EnableSmelt.Value);
        internal bool DoFarm => GetFlag(FarmKey, PluginConfig.EnableFarm.Value);
        /// <summary>Deprecated — folded into DoCollect. Kept for ZDO migration only.</summary>
        internal bool DoForage => false;
        internal bool DoGarden => GetFlag(GardenKey, PluginConfig.EnableGarden.Value);

        /// <summary>Category bitmask for Recolher (bushes + ground drops).</summary>
        internal PickupCategory PickupMask
        {
            get
            {
                if (_nview == null || !_nview.IsValid())
                {
                    return PickupCategory.DefaultMask;
                }

                var raw = _nview.GetZDO().GetInt(PickupMaskKey, -1);
                if (raw < 0)
                {
                    return PickupCategory.DefaultMask;
                }

                return (PickupCategory)raw;
            }
        }

        internal bool IsPickupAllowed(PickupCategory cat) => (PickupMask & cat) != 0;
        internal bool DoEat => GetFlag(EatKey, PluginConfig.EnableEat.Value);
        internal bool DoSitFire => GetFlag(SitFireKey, PluginConfig.EnableSitFire.Value);
        internal bool DoProtect => GetFlag(ProtectKey, PluginConfig.EnableProtect.Value);
        internal bool DoAffection => GetFlag(AffectionKey, PluginConfig.EnableAffection.Value);
        internal bool DoMusic => GetFlag(MusicKey, PluginConfig.EnableMusic.Value);
        internal bool DoRested => GetFlag(RestedKey, PluginConfig.EnableRested.Value);
        /// <summary>+comfort while wife is at home with the player (ward circle).</summary>
        internal bool DoComfort => GetFlag(ComfortKey, PluginConfig.EnableWifeComfort == null || PluginConfig.EnableWifeComfort.Value);
        /// <summary>Show wife on minimap/map in real time.</summary>
        internal bool DoMapPin => GetFlag(MapPinKey, PluginConfig.EnableWifeMapPin == null || PluginConfig.EnableWifeMapPin.Value);

        /// <summary>Per-idol lifestyle (Leisure / Balanced / Diligent). Menu cycles this.</summary>
        internal LifestyleMode Lifestyle
        {
            get
            {
                if (_nview == null || !_nview.IsValid())
                {
                    return PluginConfig.ResolveDefaultLifestyle();
                }

                var raw = _nview.GetZDO().GetInt(LifestyleKey, -1);
                if (raw >= 0)
                {
                    return PluginConfig.ClampLifestyle(raw);
                }

                // No ZDO yet: migrate legacy BasicsOnly=false → Balanced.
                if (PluginConfig.BasicsOnly != null && !PluginConfig.BasicsOnly.Value)
                {
                    return LifestyleMode.Balanced;
                }

                return PluginConfig.ResolveDefaultLifestyle();
            }
        }

        internal void CycleLifestyle()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var next = (int)Lifestyle + 1;
            if (next > 2)
            {
                next = 0;
            }

            _nview.GetZDO().Set(LifestyleKey, next);
            var mode = PluginConfig.ClampLifestyle(next);
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                ModLocalization.T("hearthwife_status_mode", PluginConfig.LifestyleLabel(mode)));
            _wife?.OnLifestyleChanged(mode);
        }

        internal static readonly string[] WifeNamePresets =
        {
            "Astrid", "Sigrid", "Ingrid", "Freyja", "Helga", "Ragna", "Thyra", "Liv",
            "Eira", "Solveig", "Yrsa", "Hilda", "Kara", "Una", "Brynhild", "Embla"
        };

        internal string WifeDisplayName
        {
            get
            {
                if (_nview != null && _nview.IsValid())
                {
                    var custom = _nview.GetZDO().GetString(NameKey, "");
                    if (!string.IsNullOrEmpty(custom))
                    {
                        return custom;
                    }

                    var idx = Mathf.Clamp(_nview.GetZDO().GetInt(NameIndexKey, 0), 0, WifeNamePresets.Length - 1);
                    return WifeNamePresets[idx];
                }

                return Localization.instance.Localize("$hearthwife_wife_name");
            }
        }

        internal void CycleWifeName()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            _nview.GetZDO().Set(NameKey, "");
            var next = (_nview.GetZDO().GetInt(NameIndexKey, 0) + 1) % WifeNamePresets.Length;
            _nview.GetZDO().Set(NameIndexKey, next);
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_name_set") + ": " + WifeNamePresets[next]);
            EnsureWife();
            _wife?.SyncDisplayName();
        }

        internal void BeginRename()
        {
            if (TextInput.instance == null)
            {
                CycleWifeName();
                return;
            }

            TextInput.instance.RequestText(new WifeNameReceiver(this), "$hearthwife_name_set", 24);
        }

        internal void SetCustomName(string name)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            name = (name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                // Empty confirm — keep current name (don't wipe custom → Astrid).
                return;
            }

            if (name.Length > 24)
            {
                name = name.Substring(0, 24);
            }

            _nview.GetZDO().Set(NameKey, name);
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_name_set") + ": " + name);
            EnsureWife();
            _wife?.SyncDisplayName();
        }

        private sealed class WifeNameReceiver : TextReceiver
        {
            private readonly WifeHome _home;

            public WifeNameReceiver(WifeHome home)
            {
                _home = home;
            }

            public string GetText() => _home != null ? _home.WifeDisplayName : "";

            public void SetText(string text)
            {
                _home?.SetCustomName(text);
            }
        }

        internal void ToggleSmelt()
        {
            SetFlag(SmeltKey, !DoSmelt);
            NotifyToggle("$hearthwife_task_smelt", DoSmelt);
        }

        internal void ToggleFarm()
        {
            SetFlag(FarmKey, !DoFarm);
            NotifyToggle("$hearthwife_task_farm", DoFarm);
        }

        /// <summary>Legacy no-op — Forragear folded into Recolher (DoCollect).</summary>
        internal void ToggleForage()
        {
            ToggleCollect();
        }

        internal void TogglePickupCategory(PickupCategory cat)
        {
            if (_nview == null || !_nview.IsValid() || cat == PickupCategory.None)
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var mask = PickupMask;
            if ((mask & cat) != 0)
            {
                mask &= ~cat;
            }
            else
            {
                mask |= cat;
            }

            _nview.GetZDO().Set(PickupMaskKey, (int)mask);
            var state = Localization.instance.Localize(
                (mask & cat) != 0 ? "$hearthwife_on" : "$hearthwife_off");
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                WifePickupFilter.LabelPt(cat) + ": " + state);
        }

        /// <summary>
        /// One-shot: if legacy Forage was ON and Collect unset/off, promote into Recolher.
        /// </summary>
        private void EnsureGatherMigration()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            var zdo = _nview.GetZDO();
            // Sentinel: PickupMaskKey never written → also means we may still need forage→collect.
            var forageOn = zdo.GetInt(ForageKey, 0) != 0;
            var collectRaw = zdo.GetInt(CollectKey, -1);
            if (!forageOn)
            {
                return;
            }

            // Promote forage into collect once, then clear forage flag.
            if (collectRaw <= 0)
            {
                if (!_nview.IsOwner())
                {
                    _nview.ClaimOwnership();
                }

                zdo.Set(CollectKey, 1);
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            zdo.Set(ForageKey, 0);
        }

        internal void ToggleGarden()
        {
            SetFlag(GardenKey, !DoGarden);
            NotifyToggle("$hearthwife_task_garden", DoGarden);
        }

        internal void ToggleEat()
        {
            SetFlag(EatKey, !DoEat);
            NotifyToggle("$hearthwife_task_eat", DoEat);
        }

        internal void ToggleSitFire()
        {
            SetFlag(SitFireKey, !DoSitFire);
            NotifyToggle("$hearthwife_task_sitfire", DoSitFire);
        }

        internal void ToggleProtect()
        {
            SetFlag(ProtectKey, !DoProtect);
            NotifyToggle("$hearthwife_task_protect", DoProtect);
        }

        internal void ToggleAffection()
        {
            SetFlag(AffectionKey, !DoAffection);
            NotifyToggle("$hearthwife_task_affection", DoAffection);
        }

        internal void ToggleMusic()
        {
            SetFlag(MusicKey, !DoMusic);
            NotifyToggle("$hearthwife_task_music", DoMusic);
        }

        internal void ToggleRested()
        {
            SetFlag(RestedKey, !DoRested);
            NotifyToggle("$hearthwife_task_rested", DoRested);
        }

        internal void ToggleComfort()
        {
            SetFlag(ComfortKey, !DoComfort);
            NotifyToggle("$hearthwife_task_comfort", DoComfort);
        }

        internal void ToggleMapPin()
        {
            SetFlag(MapPinKey, !DoMapPin);
            NotifyToggle("$hearthwife_task_mappin", DoMapPin);
        }

        /// <summary>Cooked product prefab name, or "Any".
        /// Future: optional dedicated raw-meat chest (totem-only for now).</summary>
        internal static readonly string[] CookRecipes =
        {
            "Any",
            "CookedMeat",
            "CookedFish",
            "SerpentMeatCooked",
            "CookedLoxMeat",
            "CookedEgg",
            "NeckTailGrilled",
            "CookedWolfMeat",
            "CookedDeerMeat"
        };

        internal string CookRecipeId
        {
            get
            {
                if (_nview == null || !_nview.IsValid())
                {
                    return "Any";
                }

                var i = Mathf.Clamp(_nview.GetZDO().GetInt(CookRecipeKey, 0), 0, CookRecipes.Length - 1);
                return CookRecipes[i];
            }
        }

        internal int CookRecipeIndex
        {
            get
            {
                if (_nview == null || !_nview.IsValid())
                {
                    return 0;
                }

                return Mathf.Clamp(_nview.GetZDO().GetInt(CookRecipeKey, 0), 0, CookRecipes.Length - 1);
            }
        }

        internal void CycleCookRecipe()
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var next = (CookRecipeIndex + 1) % CookRecipes.Length;
            _nview.GetZDO().Set(CookRecipeKey, next);
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_cook_recipe") + ": " + CookRecipeLabel(CookRecipes[next]));
        }

        internal static string CookRecipeLabel(string id)
        {
            if (id == "Any")
            {
                return Localization.instance.Localize("$hearthwife_cook_any");
            }

            var prefab = ObjectDB.instance?.GetItemPrefab(id);
            var shared = prefab?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
            if (!string.IsNullOrEmpty(shared))
            {
                return Localization.instance.Localize(shared);
            }

            return id;
        }

        internal void ToggleCook()
        {
            SetFlag(CookKey, !DoCook);
            NotifyToggle("$hearthwife_task_cook", DoCook);
        }

        internal void ToggleFire()
        {
            SetFlag(FireKey, !DoFire);
            NotifyToggle("$hearthwife_task_fire", DoFire);
            if (DoFire)
            {
                RequestThinkSoon();
            }
        }

        private static readonly int[] FireWoodReserveSteps = { 0, 10, 20, 30, 50 };

        /// <summary>Min fuel left in base chests (not totem). 0 = no reserve.</summary>
        internal int FireWoodReserve
        {
            get
            {
                var fallback = PluginConfig.FireWoodReserve != null
                    ? Mathf.Max(0, PluginConfig.FireWoodReserve.Value)
                    : 20;
                if (_nview == null || !_nview.IsValid())
                {
                    return fallback;
                }

                var raw = _nview.GetZDO().GetInt(FireWoodReserveKey, -1);
                return raw < 0 ? fallback : Mathf.Max(0, raw);
            }
        }

        internal void CycleFireWoodReserve(int direction)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var cur = FireWoodReserve;
            var idx = 0;
            for (var i = 0; i < FireWoodReserveSteps.Length; i++)
            {
                if (FireWoodReserveSteps[i] == cur)
                {
                    idx = i;
                    break;
                }

                if (FireWoodReserveSteps[i] < cur)
                {
                    idx = i;
                }
            }

            idx = (idx + (direction >= 0 ? 1 : -1) + FireWoodReserveSteps.Length) %
                  FireWoodReserveSteps.Length;
            _nview.GetZDO().Set(FireWoodReserveKey, FireWoodReserveSteps[idx]);
        }

        internal void ToggleMead()
        {
            SetFlag(MeadKey, !DoMead);
            NotifyToggle("$hearthwife_task_mead", DoMead);
        }

        internal void ToggleSit()
        {
            SetFlag(SitKey, !DoSit);
            NotifyToggle("$hearthwife_task_sit", DoSit);
        }

        internal void ToggleNap()
        {
            SetFlag(NapKey, !DoNap);
            NotifyToggle("$hearthwife_task_nap", DoNap);
        }

        internal bool HasAssignedBed =>
            _nview != null && _nview.IsValid() && _nview.GetZDO().GetInt(HasBedKey, 0) != 0;

        internal bool HasFishSpot =>
            _nview != null && _nview.IsValid() && _nview.GetZDO().GetInt(HasFishKey, 0) != 0;

        /// <summary>How far the fish spot may be from the idol.</summary>
        internal float FishRange => Mathf.Max(Radius, 64f);

        internal bool IsInside(Vector3 world, float padding = 0f)
        {
            var a = HomePosition;
            var b = world;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) <= Radius + padding;
        }

        /// <summary>Random dry walk point in the ward (skips lake/river bed).</summary>
        internal bool TryPickPointInRadius(float minFrac, float maxFrac, out Vector3 point)
        {
            point = HomePosition;
            var r = Radius;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var ang = Random.Range(0f, Mathf.PI * 2f);
                var dist = Random.Range(r * minFrac, r * maxFrac);
                point = HomePosition + new Vector3(Mathf.Cos(ang) * dist, 0f, Mathf.Sin(ang) * dist);
                point.y = HomePosition.y + 0.5f;
                WifeAgent.SnapToPieceOrTerrain(ref point, HomePosition.y);
                if (!WifeAgent.IsUnsafeWaterWalk(point) &&
                    !WifeAgent.IsPointNearPlayer(point, 2.0f))
                {
                    return true;
                }
            }

            // Last resort: porch in front of idol.
            point = HomePosition + transform.forward * 2f;
            WifeAgent.SnapToPieceOrTerrain(ref point, HomePosition.y);
            return !WifeAgent.IsUnsafeWaterWalk(point);
        }

        internal static bool IsBedLinkActive(WifeHome home)
        {
            return home != null && BedLinkHome == home && Time.time <= BedLinkUntil;
        }

        internal static bool TryAssignBedFromLink(Bed bed)
        {
            if (BedLinkHome == null || Time.time > BedLinkUntil || bed == null)
            {
                return false;
            }

            var home = BedLinkHome;
            if (!home.IsInside(bed.transform.position, 6f))
            {
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_bed_outside"));
                return true;
            }

            home.AssignBed(bed);
            BedLinkHome = null;
            return true;
        }

        internal void BeginBedLink()
        {
            FishLinkHome = null;
            BedLinkHome = this;
            BedLinkUntil = Time.time + 30f;
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_bed_link_hint"));
        }

        internal void AssignBed(Bed bed)
        {
            if (bed == null || _nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var p = bed.transform.position;
            var zdo = _nview.GetZDO();
            zdo.Set(HasBedKey, 1);
            zdo.Set(BedXKey, p.x);
            zdo.Set(BedYKey, p.y);
            zdo.Set(BedZKey, p.z);

            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_bed_set"));
        }

        internal void BeginFishLink()
        {
            BedLinkHome = null;
            FishLinkHome = this;
            FishLinkUntil = Time.time + 45f;
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_fish_link_hint"));
        }

        private void TickFishLink()
        {
            if (FishLinkHome != this || Time.time > FishLinkUntil)
            {
                if (FishLinkHome == this && Time.time > FishLinkUntil)
                {
                    FishLinkHome = null;
                }

                return;
            }

            var player = Player.m_localPlayer;
            if (player == null || !ZInput.GetButtonDown("Use"))
            {
                return;
            }

            // Don't steal E when linking a bed / opening something else.
            if (BedLinkHome != null)
            {
                return;
            }

            if (!Physics.Raycast(player.GetEyePoint(), player.GetLookDir(), out var hit, 50f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            if (hit.collider.GetComponentInParent<Bed>() != null ||
                hit.collider.GetComponentInParent<Container>() != null ||
                hit.collider.GetComponentInParent<WifeHome>() != null)
            {
                return;
            }

            var flat = hit.point;
            flat.y = 0f;
            var homeFlat = HomePosition;
            homeFlat.y = 0f;
            if (Vector3.Distance(flat, homeFlat) > FishRange)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.TopLeft,
                    Localization.instance.Localize("$hearthwife_fish_too_far"));
                return;
            }

            AssignFishSpot(hit.point);
            FishLinkHome = null;
        }

        internal void AssignFishSpot(Vector3 world)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var zdo = _nview.GetZDO();
            zdo.Set(HasFishKey, 1);
            zdo.Set(FishXKey, world.x);
            zdo.Set(FishYKey, world.y);
            zdo.Set(FishZKey, world.z);

            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_fish_set"));
        }

        internal bool TryGetFishSpot(out Vector3 spot)
        {
            spot = HomePosition;
            if (!HasFishSpot)
            {
                return false;
            }

            var zdo = _nview.GetZDO();
            spot = new Vector3(
                zdo.GetFloat(FishXKey),
                zdo.GetFloat(FishYKey),
                zdo.GetFloat(FishZKey));
            return true;
        }

        /// <summary>Assigned vanilla bed, or null.</summary>
        internal Bed GetAssignedBed()
        {
            if (!HasAssignedBed)
            {
                return null;
            }

            var zdo = _nview.GetZDO();
            var target = new Vector3(
                zdo.GetFloat(BedXKey),
                zdo.GetFloat(BedYKey),
                zdo.GetFloat(BedZKey));

            Bed best = null;
            var bestDist = 3.5f;
            foreach (var bed in Object.FindObjectsByType<Bed>(FindObjectsSortMode.None))
            {
                if (bed == null)
                {
                    continue;
                }

                var d = Vector3.Distance(bed.transform.position, target);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = bed;
                }
            }

            return best;
        }

        internal Transform GetSleepTarget()
        {
            var bed = GetAssignedBed();
            return bed != null ? bed.transform : null;
        }

        internal Vector3 GetSleepPosition(out Quaternion rot)
        {
            var bed = GetAssignedBed();
            if (bed == null)
            {
                rot = transform.rotation;
                return HomePosition + transform.forward * 1.5f;
            }

            // Prefer vanilla bed attach/spawn transforms.
            Transform point = null;
            foreach (var name in new[] { "spawnpoint", "SpawnPoint", "attach_bed", "AttachPoint" })
            {
                point = FindChildRecursive(bed.transform, name);
                if (point != null)
                {
                    break;
                }
            }

            if (point != null)
            {
                rot = point.rotation;
                return point.position;
            }

            // Fallback: center of mattress.
            rot = bed.transform.rotation;
            return bed.transform.position + bed.transform.up * 0.5f;
        }

        private static Transform FindChildRecursive(Transform root, string name)
        {
            if (root.name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChildRecursive(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private bool GetFlag(string key, bool defaultValue)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return defaultValue;
            }

            return _nview.GetZDO().GetInt(key, defaultValue ? 1 : 0) != 0;
        }

        private void SetFlag(string key, bool value)
        {
            if (_nview == null || !_nview.IsValid())
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            _nview.GetZDO().Set(key, value ? 1 : 0);
        }

        private void EnsureAreaMarker()
        {
            if (_areaMarker != null || !PluginConfig.ShowHomeArea.Value)
            {
                return;
            }

            GameObject guard = null;
            if (ZNetScene.instance != null)
            {
                guard = ZNetScene.instance.GetPrefab("guard_stone");
            }

            try
            {
                guard ??= Jotunn.Managers.PrefabManager.Instance.GetPrefab("guard_stone");
            }
            catch
            {
                // ignore
            }

            var src = guard != null ? guard.GetComponentInChildren<CircleProjector>(true) : null;
            if (src != null)
            {
                var go = Instantiate(src.gameObject, transform);
                go.name = "Hearthwife_HomeArea";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                foreach (var pa in go.GetComponentsInChildren<PrivateArea>(true))
                {
                    Destroy(pa);
                }

                _areaMarker = go.GetComponent<CircleProjector>();
            }
            else
            {
                var go = new GameObject("Hearthwife_HomeArea");
                go.transform.SetParent(transform, false);
                _areaMarker = go.AddComponent<CircleProjector>();
            }

            if (_areaMarker != null)
            {
                _areaMarker.m_radius = Radius;
                _areaMarker.gameObject.SetActive(false);
            }
        }

        private void UpdateAreaMarkerVisibility()
        {
            if (_areaMarker == null)
            {
                if (PluginConfig.ShowHomeArea.Value)
                {
                    EnsureAreaMarker();
                }

                return;
            }

            _areaMarker.m_radius = Radius;
            var player = Player.m_localPlayer;
            var show = PluginConfig.ShowHomeArea.Value && player != null &&
                       IsInside(player.transform.position, 8f);
            if (_areaMarker.gameObject.activeSelf != show)
            {
                _areaMarker.gameObject.SetActive(show);
            }
        }

        internal bool HasWife => _wife != null;

        /// <summary>
        /// RuneboundRest-style comfort: +N while local player and wife share this homestead ward.
        /// </summary>
        internal static int GetWifeHomeComfortBonus(Player player)
        {
            if (player == null || player != Player.m_localPlayer)
            {
                return 0;
            }

            if (PluginConfig.EnableWifeComfort != null && !PluginConfig.EnableWifeComfort.Value)
            {
                return 0;
            }

            var bonus = PluginConfig.WifeComfortBonus != null ? PluginConfig.WifeComfortBonus.Value : 2;
            if (bonus <= 0)
            {
                return 0;
            }

            foreach (var home in Object.FindObjectsByType<WifeHome>(FindObjectsSortMode.None))
            {
                if (home == null || !home.DoComfort || !home.HasWife)
                {
                    continue;
                }

                if (!home.IsInside(player.transform.position, 0.5f))
                {
                    continue;
                }

                var wife = home._wife;
                if (wife == null || wife.gameObject == null)
                {
                    continue;
                }

                // Wife must be "em casa" — inside the ward (not recalled away / despawned).
                if (!home.IsInside(wife.transform.position, 1.5f))
                {
                    continue;
                }

                return bonus;
            }

            return 0;
        }

        internal string GetWifeTestStatus()
        {
            if (_wife == null)
            {
                return "Esposa ausente — use Trazer.";
            }

            return _wife.GetTestStatusLine();
        }

        internal void ForceWifeSit()
        {
            RunWifeForce(w => w.ForceSitNow());
        }

        internal void ForceWifeStand()
        {
            RunWifeForce(w => w.ForceStandNow());
        }

        internal void ForceWifeNap()
        {
            RunWifeForce(w => w.ForceNapNow());
        }

        internal void ForceWifeSitFire()
        {
            RunWifeForce(w => w.ForceSitFireNow());
        }

        internal void ForceWifeMusic()
        {
            RunWifeForce(w => w.ForceMusicNow());
        }

        internal void ForceWifeAffection()
        {
            RunWifeForce(w => w.ForceAffectionNow());
        }

        internal void ForceWifeMorning()
        {
            RunWifeForce(w => w.ForceMorningNow());
        }

        internal void ForceWifeSleep()
        {
            RunWifeForce(w => w.ForceSleepNow());
        }

        internal void ForceWifeWake()
        {
            RunWifeForce(w => w.ForceWakeNow());
        }

        internal void ForceWifeRested()
        {
            RunWifeForce(w => w.ForceRestedNow());
        }

        internal void ForceWifeSkipWait()
        {
            RunWifeForce(w => w.ForceSkipWaitNow());
        }

        internal void ForceWifeFire()
        {
            RunWifeForce(w => w.ForceFireNow());
        }

        internal void ForceWifeCollect()
        {
            RunWifeForce(w => w.ForceCollectNow());
        }

        internal void ForceWifeRepair()
        {
            RunWifeForce(w => w.ForceRepairNow());
        }

        internal void ForceWifeCookCollect()
        {
            RunWifeForce(w => w.ForceCookCollectNow());
        }

        internal void ForceWifeCook()
        {
            RunWifeForce(w => w.ForceCookNow());
        }

        private void RunWifeForce(System.Func<WifeAgent, string> action)
        {
            if (_wife == null)
            {
                BringWife();
            }

            if (_wife == null)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center, "Esposa ausente — use Trazer.");
                return;
            }

            var msg = action(_wife);
            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, "[TESTE] " + msg);
            Jotunn.Logger.LogInfo("Hearthwife TEST: " + msg);
        }

        internal void BringWife()
        {
            // One button: spawn if missing, otherwise pull/unstuck to the idol.
            if (_wife != null)
            {
                try
                {
                    if (_wife.gameObject != null)
                    {
                        RecallWife();
                        return;
                    }
                }
                catch
                {
                    _wife = null;
                }
            }

            ForceSpawnWife();
        }

        internal void ForceSpawnWife()
        {
            if (_wife != null)
            {
                try
                {
                    if (_wife.gameObject != null)
                    {
                        RecallWife();
                        return;
                    }
                }
                catch
                {
                    try
                    {
                        _wife.Despawn();
                    }
                    catch
                    {
                    }

                    _wife = null;
                }
            }

            if (!WifeLimits.CanSpawnWifeFor(this))
            {
                WifeLimits.CullExtraWives(this);
                if (!WifeLimits.CanSpawnWifeFor(this))
                {
                    WifeLimits.NotifyWifeLimit();
                    return;
                }
            }

            _wife = null;
            _spawnFails = 0;
            WifeNpcPrefab.ForceReregister();
            EnsureWife();
            if (_wife != null)
            {
                ApplyLooksToWife();
            }
        }

        internal void ToggleRepair()
        {
            SetFlag(RepairKey, !DoRepair);
        }

        internal void ToggleCollect()
        {
            EnsureGatherMigration();
            var next = !GetFlag(CollectKey,
                PluginConfig.EnableCollect.Value || PluginConfig.EnableForage.Value);
            SetFlag(CollectKey, next);
            // Keep legacy forage flag clear so migration does not re-enable.
            SetFlag(ForageKey, false);
            NotifyToggle("$hearthwife_task_collect", next);
        }

        internal void ToggleFish()
        {
            SetFlag(FishKey, !DoFish);
        }

        private void EnsureWife()
        {
            // Clear destroyed Unity refs.
            if (_wife != null)
            {
                try
                {
                    if (_wife.gameObject == null)
                    {
                        _wife = null;
                    }
                }
                catch
                {
                    _wife = null;
                }
            }

            if (_wife != null || Player.m_localPlayer == null)
            {
                return;
            }

            // Reclaim if _wife ref was lost but the agent is still alive.
            _wife = WifeLimits.FindExistingWifeFor(this);
            if (_wife != null)
            {
                return;
            }

            WifeLimits.CullExtraWives(this);
            if (!WifeLimits.CanSpawnWifeFor(this))
            {
                // Quiet retry — do not spam center message every Update tick.
                _spawnRetryAt = Time.time + 45f;
                return;
            }

            try
            {
                WifeNpcPrefab.Register();
                _wife = WifeAgent.Spawn(this);
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError("Hearthwife: spawn exception: " + ex);
                _wife = null;
            }

            if (_wife != null)
            {
                _spawnFails = 0;
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.Center,
                    Localization.instance.Localize("$hearthwife_spawned"));
            }
            else if (++_spawnFails <= 8)
            {
                Jotunn.Logger.LogWarning("Hearthwife: wife spawn failed (#" + _spawnFails + ")");
            }
        }

        internal int DressIndex => GetIndex(DressKey, AppearKey, WifeLooks.DressCount);
        internal int HairIndex => GetIndex(HairKey, null, WifeLooks.HairCount);
        internal int HairColorIndex => GetIndex(HairColorKey, null, WifeLooks.HairColorCount);
        internal int SkinIndex => GetIndex(SkinKey, null, WifeLooks.SkinCount);

        /// <summary>Legacy single index (dress).</summary>
        internal int AppearanceIndex => DressIndex;

        private int GetIndex(string key, string legacyKey, int count)
        {
            if (_nview == null || !_nview.IsValid() || count < 1)
            {
                return 0;
            }

            var zdo = _nview.GetZDO();
            var v = zdo.GetInt(key, -1);
            if (v < 0 && legacyKey != null)
            {
                v = zdo.GetInt(legacyKey, 0);
            }

            if (v < 0)
            {
                v = 0;
            }

            return Mathf.Clamp(v, 0, count - 1);
        }

        private void SetIndex(string key, int value, int count)
        {
            if (_nview == null || !_nview.IsValid() || count < 1)
            {
                return;
            }

            if (!_nview.IsOwner())
            {
                _nview.ClaimOwnership();
            }

            var zdo = _nview.GetZDO();
            var clamped = Mathf.Clamp(value, 0, count - 1);
            zdo.Set(key, clamped);
            // Force persistent save of look ints (same durability as custom name string).
            zdo.Set(key + "_saved", 1);
        }

        internal void ApplyLooksToWife()
        {
            EnsureWife();
            if (_wife == null)
            {
                return;
            }

            _wife.InvalidateLooksCache();
            _wife.ApplyAppearance(DressIndex, HairIndex, HairColorIndex, SkinIndex);
        }

        /// <summary>Opened Visual tab — soft-call her to the idol porch if far.</summary>
        internal void BeginLookPresentSession()
        {
            EnsureWife();
            _wife?.CallForLookPresent(this);
        }

        /// <summary>Left Visual / closed menu.</summary>
        internal void EndLookPresentSession()
        {
            _wife?.ReleaseLookPresent();
        }

        internal void CycleDress() => StepDress(1);

        internal void CycleDressBack() => StepDress(-1);

        private void StepDress(int delta)
        {
            var next = (DressIndex + delta) % WifeLooks.DressCount;
            if (next < 0)
            {
                next += WifeLooks.DressCount;
            }

            SetIndex(DressKey, next, WifeLooks.DressCount);
            SetIndex(AppearKey, next, WifeLooks.DressCount);
            ApplyLooksToWife();
            BeginLookPresentSession();
        }

        internal void CycleHair() => StepHair(1);

        internal void CycleHairBack() => StepHair(-1);

        private void StepHair(int delta)
        {
            var next = (HairIndex + delta) % WifeLooks.HairCount;
            if (next < 0)
            {
                next += WifeLooks.HairCount;
            }

            SetIndex(HairKey, next, WifeLooks.HairCount);
            ApplyLooksToWife();
            BeginLookPresentSession();
        }

        internal void CycleHairColor() => StepHairColor(1);

        internal void CycleHairColorBack() => StepHairColor(-1);

        private void StepHairColor(int delta)
        {
            var next = (HairColorIndex + delta) % WifeLooks.HairColorCount;
            if (next < 0)
            {
                next += WifeLooks.HairColorCount;
            }

            SetIndex(HairColorKey, next, WifeLooks.HairColorCount);
            ApplyLooksToWife();
            BeginLookPresentSession();
        }

        internal void CycleSkin() => StepSkin(1);

        internal void CycleSkinBack() => StepSkin(-1);

        private void StepSkin(int delta)
        {
            var next = (SkinIndex + delta) % WifeLooks.SkinCount;
            if (next < 0)
            {
                next += WifeLooks.SkinCount;
            }

            SetIndex(SkinKey, next, WifeLooks.SkinCount);
            ApplyLooksToWife();
            BeginLookPresentSession();
        }

        private static void NotifyPart(string token, int n, int max)
        {
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize(token, n.ToString(), max.ToString()));
        }

        internal void RecallWife()
        {
            EnsureWife();
            if (_wife == null)
            {
                MessageHud.instance?.ShowMessage(
                    MessageHud.MessageType.TopLeft,
                    Localization.instance.Localize("$hearthwife_recall_fail"));
                return;
            }

            _wife.RecallToHome(this);
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize("$hearthwife_recalled"));
        }

        internal void CycleAppearance() => CycleDress();

        internal void OpenChest(Humanoid user)
        {
            if (_container == null)
            {
                _container = GetComponent<Container>();
            }

            if (_container != null && user is Player)
            {
                InventoryGui.instance?.Show(_container);
            }
        }

        private void RunMenuAction()
        {
            switch ((MenuSlot)_menuIndex)
            {
                case MenuSlot.Dress:
                    CycleDress();
                    break;
                case MenuSlot.Hair:
                    CycleHair();
                    break;
                case MenuSlot.HairColor:
                    CycleHairColor();
                    break;
                case MenuSlot.Skin:
                    CycleSkin();
                    break;
                case MenuSlot.Recall:
                    RecallWife();
                    break;
                case MenuSlot.SetBed:
                    BeginBedLink();
                    break;
                case MenuSlot.ToggleRepair:
                    SetFlag(RepairKey, !DoRepair);
                    NotifyToggle("$hearthwife_task_repair", DoRepair);
                    break;
                case MenuSlot.ToggleCollect:
                    ToggleCollect();
                    break;
                case MenuSlot.ToggleFish:
                    SetFlag(FishKey, !DoFish);
                    NotifyToggle("$hearthwife_task_fish", DoFish);
                    break;
            }

            _menuIndex = (_menuIndex + 1) % MenuCount;
        }

        private static void NotifyToggle(string taskToken, bool on)
        {
            var state = Localization.instance.Localize(on ? "$hearthwife_on" : "$hearthwife_off");
            MessageHud.instance?.ShowMessage(
                MessageHud.MessageType.TopLeft,
                Localization.instance.Localize(taskToken) + ": " + state);
        }

        /// <summary>On-demand garden/beehive report (Runa-style inspect, separate from harvest).</summary>
        internal void InspectGarden()
        {
            var bad = 0;
            var healthy = 0;
            var beesTight = 0;
            var honeyReady = 0;

            foreach (var plant in Object.FindObjectsByType<Plant>(FindObjectsSortMode.None))
            {
                if (plant == null || !IsInside(plant.transform.position))
                {
                    continue;
                }

                try
                {
                    if (plant.GetStatus() == Plant.Status.Healthy)
                    {
                        healthy++;
                    }
                    else
                    {
                        bad++;
                    }
                }
                catch
                {
                }
            }

            foreach (var hive in Object.FindObjectsByType<Beehive>(FindObjectsSortMode.None))
            {
                if (hive == null || !IsInside(hive.transform.position))
                {
                    continue;
                }

                try
                {
                    if (!hive.HaveFreeSpace())
                    {
                        beesTight++;
                    }

                    if (hive.GetHoneyLevel() >= Mathf.Max(1, hive.m_maxHoney / 2))
                    {
                        honeyReady++;
                    }
                }
                catch
                {
                }
            }

            string line;
            if (bad == 0 && beesTight == 0 && honeyReady == 0)
            {
                line = healthy > 0
                    ? Localization.instance.Localize("$hearthwife_garden_ok", healthy.ToString())
                    : Localization.instance.Localize("$hearthwife_garden_empty");
            }
            else
            {
                line = Localization.instance.Localize(
                    "$hearthwife_garden_report",
                    bad.ToString(),
                    beesTight.ToString(),
                    honeyReady.ToString());
            }

            MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, line);
            if (_wife != null)
            {
                WifeTalk.Say(_wife.gameObject, line);
            }
        }

        private string MenuLabel()
        {
            switch ((MenuSlot)_menuIndex)
            {
                case MenuSlot.Dress:
                    return Localization.instance.Localize("$hearthwife_menu_dress");
                case MenuSlot.Hair:
                    return Localization.instance.Localize("$hearthwife_menu_hair");
                case MenuSlot.HairColor:
                    return Localization.instance.Localize("$hearthwife_menu_haircolor");
                case MenuSlot.Skin:
                    return Localization.instance.Localize("$hearthwife_menu_skin");
                case MenuSlot.Recall:
                    return Localization.instance.Localize("$hearthwife_menu_recall");
                case MenuSlot.SetBed:
                    return Localization.instance.Localize("$hearthwife_menu_setbed");
                case MenuSlot.ToggleRepair:
                    return Localization.instance.Localize("$hearthwife_menu_repair");
                case MenuSlot.ToggleCollect:
                    return Localization.instance.Localize("$hearthwife_menu_collect");
                case MenuSlot.ToggleFish:
                    return Localization.instance.Localize("$hearthwife_menu_fish");
                default:
                    return Localization.instance.Localize("$hearthwife_menu_dress");
            }
        }

        private static string OnOff(bool v) =>
            Localization.instance.Localize(v ? "$hearthwife_on" : "$hearthwife_off");

        public string GetHoverText()
        {
            // Menu open: hide world hover (was drawing "desbugar esposa" beside the panel).
            if (WifeMenu.IsOpen)
            {
                return "";
            }

            var text = Localization.instance.Localize("$hearthwife_idol_name")
                       + "\n"
                       + Localization.instance.Localize("$hearthwife_area_hint");

            if (!WifeLimits.IsAllowedIdol(this))
            {
                text += "\n" + Localization.instance.Localize("$hearthwife_idol_excess");
            }
            else
            {
                text += "\n" + Localization.instance.Localize("$hearthwife_area_zone");
            }

            if (IsStorageFull)
            {
                text += "\n" + Localization.instance.Localize("$hearthwife_chest_full_status");
            }

            text += "\n" + Localization.instance.Localize("$hearthwife_idol_break")
                    + "\n[<color=yellow><b>E</b></color>] "
                    + Localization.instance.Localize("$hearthwife_open_chest")
                    + "\n[<color=yellow><b>Shift+E</b></color>] "
                    + Localization.instance.Localize("$hearthwife_open_menu")
                    + "\n" + Localization.instance.Localize("$hearthwife_bind_recall")
                    + Localization.instance.Localize("$hearthwife_menu_recall");
            return text;
        }

        public string GetHoverName() => Localization.instance.Localize("$hearthwife_idol_name");

        public float GetHoverOffset() => 0.5f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
            {
                return false;
            }

            if (user is Player player && player.IsCrouching())
            {
                RecallWife();
                return true;
            }

            // Shift+E = menu, E = chest.
            if (alt)
            {
                WifeMenu.Toggle(this);
                return true;
            }

            OpenChest(user);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        internal void Init(bool isIdol)
        {
        }
    }
}
