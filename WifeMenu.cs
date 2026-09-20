using System.Collections;
using System.Collections.Generic;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.UI;

namespace Hearthwife
{
    /// <summary>
    /// Homestead wife menu — wood panel, VLG scroll content (no manual Y stacking).
    /// </summary>
    internal static class WifeMenu
    {
        private enum Tab
        {
            Home = 0,
            Look = 1,
            Work = 2,
            Life = 3,
            Debug = 4
        }

        private static GameObject _root;
        private static GameObject _scrollArea;
        private static RectTransform _scrollContent;
        private static ScrollRect _scrollRect;
        private static VerticalLayoutGroup _contentLayout;
        private static WifeHome _home;
        private static Text _title;
        private static Text _hint;
        private static Text _pageTitle;
        private static Text _status;
        private static readonly List<GameObject> _pageWidgets = new List<GameObject>();
        private static readonly List<GameObject> _tabButtons = new List<GameObject>();
        private static Tab _tab = Tab.Home;
        private static bool _built;

        /// <summary>Bump when panel/scroll/tabs change so stale UI rebuilds.</summary>
        private const int UiVersion = 22;

        private const float PanelW = 560f;
        private const float PanelH = 600f;
        private const float ContentTop = -148f;
        private const float ScrollH = 370f;
        private const float ScrollInnerW = 468f;
        private const float FootY = -548f;
        private const float RowH = 40f;
        private const float BtnH = 36f;
        private const float ScrollSensitivity = 90f;
        private const float LayoutSpacing = 8f;

        private const int FontTitle = 28;
        private const int FontHint = 13;
        private const int FontPage = 20;
        private const int FontSection = 18;
        private const int FontBody = 14;
        private const int FontTab = 15;
        private const int FontToggleBtn = 15;

        // Role colors — section ≠ item ≠ status ≠ hint.
        private static readonly Color ColSection = new Color(1f, 0.74f, 0.32f, 1f);
        private static readonly Color ColSectionBand = new Color(0.12f, 0.06f, 0.03f, 0.5f);
        private static readonly Color ColDivider = new Color(0.72f, 0.48f, 0.22f, 0.55f);
        private static readonly Color ColItemLabel = new Color(0.84f, 0.76f, 0.64f, 1f);
        private static readonly Color ColToggleBand = new Color(0.06f, 0.04f, 0.02f, 0.28f);
        private static readonly Color ColStatus = new Color(0.72f, 0.68f, 0.58f, 1f);
        private static readonly Color ColStatusBand = new Color(0.04f, 0.03f, 0.02f, 0.35f);
        private static readonly Color ColHint = new Color(0.62f, 0.56f, 0.48f, 1f);
        private static readonly Color ColActionBand = new Color(0.18f, 0.10f, 0.04f, 0.4f);

        internal static bool IsOpen => _root != null && _root.activeSelf;

        internal static void Open(WifeHome home)
        {
            if (home == null)
            {
                return;
            }

            _home = home;
            if (_root != null && _root.name != "Hearthwife_Menu_v" + UiVersion)
            {
                Invalidate();
            }

            EnsureUi();
            if (_root == null)
            {
                return;
            }

            ShowTab(_tab);
            _root.SetActive(true);
            GUIManager.BlockInput(true);
        }

        internal static void Close()
        {
            if (_tab == Tab.Look)
            {
                _home?.EndLookPresentSession();
            }

            if (_root != null)
            {
                _root.SetActive(false);
            }

            GUIManager.BlockInput(false);
            _home = null;
        }

        internal static void Toggle(WifeHome home)
        {
            if (IsOpen && _home == home)
            {
                Close();
            }
            else
            {
                Open(home);
            }
        }

        internal static void Invalidate()
        {
            Close();
            if (_root != null)
            {
                Object.Destroy(_root);
            }

            _root = null;
            _scrollArea = null;
            _scrollContent = null;
            _scrollRect = null;
            _contentLayout = null;
            _built = false;
            _pageWidgets.Clear();
            _tabButtons.Clear();
        }

        internal static void TickCloseKeys()
        {
            if (!IsOpen)
            {
                return;
            }

            if (ZInput.GetKeyDown(KeyCode.Escape) ||
                ZInput.GetButtonDown("JoyButtonB") ||
                ZInput.GetButtonDown("Inventory"))
            {
                Close();
            }
        }

        private static void EnsureUi()
        {
            if (_built && _root != null)
            {
                return;
            }

            if (GUIManager.Instance == null || GUIManager.CustomGUIFront == null)
            {
                Jotunn.Logger.LogWarning("Hearthwife: GUIManager not ready");
                return;
            }

            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            _pageWidgets.Clear();
            _tabButtons.Clear();

            var gm = GUIManager.Instance;
            _root = gm.CreateWoodpanel(
                parent: GUIManager.CustomGUIFront.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: PanelW,
                height: PanelH,
                draggable: true);
            _root.name = "Hearthwife_Menu_v" + UiVersion;
            ApplyHomesteadPanelLook(_root);

            _title = MakeFixedText(ModLocalization.T("hearthwife_menu_title"), 0f, -26f, FontTitle, 500f, 34f);
            _hint = MakeFixedText(
                ModLocalization.T("hearthwife_menu_hint"),
                0f, -56f, FontHint, 520f, 22f);

            RebuildTabs();
            _pageTitle = MakeFixedText("", 0f, -128f, FontPage, 500f, 28f);

            BuildScrollArea();
            if (_scrollRect == null || _scrollContent == null)
            {
                Jotunn.Logger.LogError("Hearthwife: menu scroll failed — UI not marked built");
                if (_root != null)
                {
                    Object.Destroy(_root);
                    _root = null;
                }

                _built = false;
                return;
            }

            MakeFixedBtn(ModLocalization.T("hearthwife_btn_bring"), -170f, FootY, 120f, 38f, () =>
            {
                _home?.BringWife();
                RefreshStatus();
            });
            MakeFixedBtn(ModLocalization.T("hearthwife_btn_chest"), -25f, FootY, 120f, 38f, () =>
            {
                var player = Player.m_localPlayer;
                if (player != null && _home != null)
                {
                    _home.OpenChest(player);
                }
            });
            MakeFixedBtn(ModLocalization.T("hearthwife_btn_close"), 130f, FootY, 120f, 38f, Close);

            _root.SetActive(false);
            _built = true;
        }

        private static void RebuildTabs()
        {
            foreach (var go in _tabButtons)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _tabButtons.Clear();
            const float tabY = -90f;
            const float tabW = 98f;
            const float tabH = 34f;
            _tabButtons.Add(MakeTabBtn(ModLocalization.T("hearthwife_menu_tab_home"), -200f, tabY, tabW, tabH, Tab.Home));
            _tabButtons.Add(MakeTabBtn(ModLocalization.T("hearthwife_menu_tab_look"), -100f, tabY, tabW, tabH, Tab.Look));
            _tabButtons.Add(MakeTabBtn(ModLocalization.T("hearthwife_menu_tab_work"), 0f, tabY, tabW, tabH, Tab.Work));
            _tabButtons.Add(MakeTabBtn(ModLocalization.T("hearthwife_menu_tab_life"), 100f, tabY, tabW, tabH, Tab.Life));
            // Debug only when config is ON (default OFF for pre-1.0).
            if (PluginConfig.ShowDebugTab != null && PluginConfig.ShowDebugTab.Value)
            {
                _tabButtons.Add(MakeTabBtn(ModLocalization.T("hearthwife_menu_tab_debug"), 200f, tabY, tabW, tabH, Tab.Debug));
            }
            else if (_tab == Tab.Debug)
            {
                _tab = Tab.Home;
            }
        }

        private static GameObject MakeTabBtn(string label, float x, float y, float w, float h, Tab tab)
        {
            var active = _tab == tab;
            var text = active ? "· " + label + " ·" : label;
            var go = MakeFixedBtn(text, x, y, w, h, () => ShowTab(tab));
            var texts = go.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                t.fontSize = active ? FontTab + 1 : FontTab;
                t.color = active
                    ? GUIManager.Instance.ValheimOrange
                    : new Color(0.85f, 0.78f, 0.65f, 1f);
            }

            return go;
        }

        private static void BuildScrollArea()
        {
            var gm = GUIManager.Instance;

            _scrollArea = gm.CreateScrollView(
                parent: _root.transform,
                showHorizontalScrollbar: false,
                showVerticalScrollbar: true,
                handleSize: 12f,
                handleDistanceToBorder: 4f,
                handleColors: gm.ValheimScrollbarHandleColorBlock,
                slidingAreaBackgroundColor: new Color(0f, 0f, 0f, 0.35f),
                width: ScrollInnerW + 24f,
                height: ScrollH);
            _scrollArea.name = "Hearthwife_Scroll";

            var canvasRt = _scrollArea.GetComponent<RectTransform>();
            canvasRt.anchorMin = new Vector2(0.5f, 1f);
            canvasRt.anchorMax = new Vector2(0.5f, 1f);
            canvasRt.pivot = new Vector2(0.5f, 1f);
            canvasRt.anchoredPosition = new Vector2(0f, ContentTop);
            canvasRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, ScrollInnerW + 24f);
            canvasRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, ScrollH);

            var scrollViewTf = _scrollArea.transform.Find("Scroll View");
            if (scrollViewTf != null)
            {
                var svRt = scrollViewTf.GetComponent<RectTransform>();
                svRt.anchorMin = Vector2.zero;
                svRt.anchorMax = Vector2.one;
                svRt.pivot = new Vector2(0.5f, 0.5f);
                svRt.anchoredPosition = Vector2.zero;
                svRt.offsetMin = Vector2.zero;
                svRt.offsetMax = Vector2.zero;

                var svImg = scrollViewTf.GetComponent<Image>();
                if (svImg != null)
                {
                    svImg.color = new Color(1f, 1f, 1f, 0.02f);
                }
            }

            _scrollRect = _scrollArea.GetComponentInChildren<ScrollRect>(true);
            if (_scrollRect == null || _scrollRect.content == null)
            {
                Jotunn.Logger.LogError("Hearthwife: CreateScrollView missing ScrollRect/content");
                return;
            }

            _scrollContent = _scrollRect.content;

            // Nested Canvas breaks Mask / can render off-GUI. Keep VLG + CSF for layout.
            StripImmediate(_scrollContent.GetComponent<Canvas>());
            StripImmediate(_scrollContent.GetComponent<GraphicRaycaster>());

            _contentLayout = _scrollContent.GetComponent<VerticalLayoutGroup>();
            if (_contentLayout == null)
            {
                _contentLayout = _scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            _contentLayout.padding = new RectOffset(10, 10, 8, 12);
            _contentLayout.spacing = LayoutSpacing;
            _contentLayout.childAlignment = TextAnchor.UpperCenter;
            _contentLayout.childControlWidth = true;
            _contentLayout.childControlHeight = true;
            _contentLayout.childForceExpandWidth = true;
            _contentLayout.childForceExpandHeight = false;

            var csf = _scrollContent.GetComponent<ContentSizeFitter>();
            if (csf == null)
            {
                csf = _scrollContent.gameObject.AddComponent<ContentSizeFitter>();
            }

            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _scrollContent.anchorMin = new Vector2(0f, 1f);
            _scrollContent.anchorMax = new Vector2(1f, 1f);
            _scrollContent.pivot = new Vector2(0.5f, 1f);
            _scrollContent.anchoredPosition = Vector2.zero;
            _scrollContent.sizeDelta = new Vector2(0f, ScrollH);

            if (_scrollRect.viewport != null)
            {
                var vp = _scrollRect.viewport;
                StripImmediate(vp.GetComponent<Mask>());

                vp.anchorMin = Vector2.zero;
                vp.anchorMax = Vector2.one;
                vp.pivot = new Vector2(0.5f, 0.5f);
                vp.anchoredPosition = Vector2.zero;
                vp.offsetMin = new Vector2(4f, 4f);
                vp.offsetMax = new Vector2(-18f, -4f);

                var vpImg = vp.GetComponent<Image>();
                if (vpImg != null)
                {
                    vpImg.color = new Color(0f, 0f, 0f, 0f);
                    vpImg.raycastTarget = false;
                }
            }

            gm.ApplyScrollRectStyle(_scrollRect);
            _scrollRect.scrollSensitivity = ScrollSensitivity;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.inertia = true;
            _scrollRect.decelerationRate = 0.135f;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

            if (_scrollRect.verticalScrollbar != null)
            {
                gm.ApplyScrollbarStyle(_scrollRect.verticalScrollbar);
                var barRt = _scrollRect.verticalScrollbar.transform as RectTransform;
                if (barRt != null)
                {
                    barRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 12f);
                }

                var handle = _scrollRect.verticalScrollbar.handleRect;
                if (handle != null)
                {
                    handle.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 10f);
                }
            }

            if (!ValidateScrollIntegrity())
            {
                Jotunn.Logger.LogError("Hearthwife: scroll integrity failed after setup");
                _scrollRect = null;
                _scrollContent = null;
            }
        }

        private static void ApplyHomesteadPanelLook(GameObject panel)
        {
            if (panel == null)
            {
                return;
            }

            var img = panel.GetComponent<Image>();
            if (img == null)
            {
                return;
            }

            var settings = GUIManager.Instance.GetSprite("woodpanel_settings");
            if (settings != null)
            {
                img.sprite = settings;
                img.type = Image.Type.Sliced;
            }

            img.color = new Color(1f, 0.93f, 0.84f, 1f);

            var washGo = new GameObject("Hearthwife_HearthWash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            washGo.transform.SetParent(panel.transform, false);
            washGo.transform.SetAsFirstSibling();

            var washRt = washGo.GetComponent<RectTransform>();
            washRt.anchorMin = Vector2.zero;
            washRt.anchorMax = Vector2.one;
            washRt.pivot = new Vector2(0.5f, 0.5f);
            washRt.anchoredPosition = Vector2.zero;
            washRt.offsetMin = new Vector2(12f, 12f);
            washRt.offsetMax = new Vector2(-12f, -12f);

            var wash = washGo.GetComponent<Image>();
            wash.color = new Color(0.42f, 0.18f, 0.10f, 0.16f);
            wash.raycastTarget = false;
        }

        private static bool ValidateScrollIntegrity()
        {
            if (_scrollRect == null || _scrollContent == null)
            {
                return false;
            }

            if (_scrollRect.viewport != null)
            {
                var badMask = _scrollRect.viewport.GetComponent<Mask>();
                if (badMask != null)
                {
                    Jotunn.Logger.LogWarning("Hearthwife: stripping Viewport Mask (would hide buttons)");
                    StripImmediate(badMask);
                }
            }

            if (_scrollContent.GetComponent<Canvas>() != null)
            {
                Jotunn.Logger.LogWarning("Hearthwife: stripping nested Content Canvas");
                StripImmediate(_scrollContent.GetComponent<Canvas>());
                StripImmediate(_scrollContent.GetComponent<GraphicRaycaster>());
            }

            if (_scrollContent.GetComponent<VerticalLayoutGroup>() == null)
            {
                Jotunn.Logger.LogError("Hearthwife: Content missing VerticalLayoutGroup");
                return false;
            }

            return _scrollContent.GetComponent<Canvas>() == null &&
                   (_scrollRect.viewport == null || _scrollRect.viewport.GetComponent<Mask>() == null);
        }

        private static void StripImmediate(Component c)
        {
            if (c != null)
            {
                Object.DestroyImmediate(c);
            }
        }

        private static void ResetScrollToTop()
        {
            ApplyScrollPixels(0f);
        }

        /// <summary>Pixels scrolled down from the top (content.anchoredPosition.y).</summary>
        private static float CaptureScrollPixels()
        {
            if (_scrollContent == null)
            {
                return 0f;
            }

            return Mathf.Max(0f, _scrollContent.anchoredPosition.y);
        }

        private static void ApplyScrollPixels(float pixelsFromTop)
        {
            if (_scrollRect == null || _scrollContent == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            if (_contentLayout != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
            }

            Canvas.ForceUpdateCanvases();

            var contentH = _scrollContent.rect.height;
            var viewH = _scrollRect.viewport != null
                ? _scrollRect.viewport.rect.height
                : _scrollRect.GetComponent<RectTransform>().rect.height;
            var maxY = Mathf.Max(0f, contentH - viewH);
            var y = Mathf.Clamp(pixelsFromTop, 0f, maxY);

            _scrollRect.StopMovement();
            _scrollRect.velocity = Vector2.zero;
            _scrollContent.anchoredPosition = new Vector2(_scrollContent.anchoredPosition.x, y);

            if (maxY > 0.5f)
            {
                _scrollRect.verticalNormalizedPosition = 1f - (y / maxY);
            }
            else
            {
                _scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private static void FinishPageLayout(float scrollPixelsFromTop = 0f, bool keepScroll = false)
        {
            Canvas.ForceUpdateCanvases();
            if (_contentLayout != null && _scrollContent != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
            }

            Canvas.ForceUpdateCanvases();

            if (keepScroll)
            {
                ApplyScrollPixels(scrollPixelsFromTop);
                // Layout often settles one frame later — re-apply so toggles don't jump.
                if (_home != null)
                {
                    _home.StartCoroutine(CoRestoreScroll(scrollPixelsFromTop));
                }
            }
            else
            {
                ApplyScrollPixels(0f);
            }
        }

        private static IEnumerator CoRestoreScroll(float pixelsFromTop)
        {
            yield return null;
            if (!IsOpen || _scrollContent == null)
            {
                yield break;
            }

            ApplyScrollPixels(pixelsFromTop);
            yield return null;
            if (!IsOpen || _scrollContent == null)
            {
                yield break;
            }

            ApplyScrollPixels(pixelsFromTop);
        }

        /// <param name="keepScroll">
        /// True when refreshing the same tab (toggle/cycle) — keep scrollbar where the player was.
        /// False when switching tabs or opening the menu.
        /// </param>
        private static void ShowTab(Tab tab, bool keepScroll = false)
        {
            var sameTab = tab == _tab;
            var savedPixels = 0f;
            if (keepScroll && sameTab)
            {
                savedPixels = CaptureScrollPixels();
            }

            var leavingLook = _tab == Tab.Look && tab != Tab.Look;
            var enteringLook = tab == Tab.Look;
            _tab = tab;
            ClearPageWidgets();
            RebuildTabs();
            RefreshStatus();

            if (leavingLook)
            {
                _home?.EndLookPresentSession();
            }
            else if (enteringLook)
            {
                _home?.BeginLookPresentSession();
            }

            switch (tab)
            {
                case Tab.Home:
                    Set(_pageTitle, ModLocalization.T("hearthwife_menu_page_home"));
                    AddStatusBlock(2);
                    AddSection(ModLocalization.T("hearthwife_menu_section_lifestyle"));
                    AddFullBtn(
                        ModLocalization.T("hearthwife_menu_mode_btn", PluginConfig.LifestyleLabel(_home.Lifestyle)),
                        () =>
                        {
                            _home?.CycleLifestyle();
                            ShowTab(Tab.Home, keepScroll: true);
                        });
                    AddBodyText(PluginConfig.LifestyleHint(_home.Lifestyle), 2);
                    AddSection(ModLocalization.T("hearthwife_menu_section_namebed"));
                    AddRow2(ModLocalization.T("hearthwife_menu_write_name"), () =>
                    {
                        _home?.BeginRename();
                        Close();
                    }, ModLocalization.T("hearthwife_menu_name_list"), () =>
                    {
                        _home?.CycleWifeName();
                        RefreshStatus();
                    });
                    AddFullBtn(ModLocalization.T("hearthwife_menu_set_bed"), () =>
                    {
                        _home?.BeginBedLink();
                        Close();
                    });
                    break;

                case Tab.Look:
                    Set(_pageTitle, ModLocalization.T("hearthwife_menu_page_look"));
                    AddStatusBlock(2);
                    AddBodyText(ModLocalization.T("hearthwife_menu_look_hint"), 2);
                    AddSection(ModLocalization.T("hearthwife_menu_section_visual"));
                    AddCycleRow(ModLocalization.T("hearthwife_menu_label_dress"),
                        (_home.DressIndex + 1) + " / " + WifeLooks.DressCount,
                        () => { _home?.CycleDressBack(); ShowTab(Tab.Look, keepScroll: true); },
                        () => { _home?.CycleDress(); ShowTab(Tab.Look, keepScroll: true); });
                    AddCycleRow(ModLocalization.T("hearthwife_menu_label_hair"),
                        (_home.HairIndex + 1) + " / " + WifeLooks.HairCount,
                        () => { _home?.CycleHairBack(); ShowTab(Tab.Look, keepScroll: true); },
                        () => { _home?.CycleHair(); ShowTab(Tab.Look, keepScroll: true); });
                    AddCycleRow(ModLocalization.T("hearthwife_menu_label_haircolor"),
                        (_home.HairColorIndex + 1) + " / " + WifeLooks.HairColorCount,
                        () => { _home?.CycleHairColorBack(); ShowTab(Tab.Look, keepScroll: true); },
                        () => { _home?.CycleHairColor(); ShowTab(Tab.Look, keepScroll: true); });
                    AddCycleRow(ModLocalization.T("hearthwife_menu_label_skin"),
                        (_home.SkinIndex + 1) + " / " + WifeLooks.SkinCount,
                        () => { _home?.CycleSkinBack(); ShowTab(Tab.Look, keepScroll: true); },
                        () => { _home?.CycleSkin(); ShowTab(Tab.Look, keepScroll: true); });
                    break;

                case Tab.Work:
                    Set(_pageTitle, ModLocalization.T("hearthwife_menu_page_work"));
                    AddStatusBlock(2);
                    if (!PluginConfig.AllowsWork(_home.Lifestyle))
                    {
                        AddBodyText(ModLocalization.T("hearthwife_menu_work_paused"), 2);
                    }

                    // One homestead block — fire / repair / cook (no orphan "extras" / recipe rows).
                    AddSection(ModLocalization.T("hearthwife_menu_section_athome"));
                    AddToggle(ModLocalization.T("hearthwife_menu_toggle_fire"), () => _home.DoFire, () => _home.ToggleFire());
                    if (_home.DoFire)
                    {
                        AddCycleRow(
                            ModLocalization.T("hearthwife_menu_wood_reserve"),
                            _home.FireWoodReserve <= 0
                                ? ModLocalization.T("hearthwife_menu_wood_none")
                                : ModLocalization.T("hearthwife_menu_wood_units", _home.FireWoodReserve.ToString()),
                            () =>
                            {
                                _home?.CycleFireWoodReserve(-1);
                                ShowTab(Tab.Work, keepScroll: true);
                            },
                            () =>
                            {
                                _home?.CycleFireWoodReserve(1);
                                ShowTab(Tab.Work, keepScroll: true);
                            });
                    }

                    AddToggle(ModLocalization.T("hearthwife_menu_toggle_repair"), () => _home.DoRepair, () => _home.ToggleRepair());
                    AddToggle(ModLocalization.T("hearthwife_menu_toggle_cook"), () => _home.DoCook, () => _home.ToggleCook());

                    AddSection(ModLocalization.T("hearthwife_menu_section_gather"));
                    AddToggle(ModLocalization.T("hearthwife_menu_toggle_collect"), () => _home.DoCollect, () => _home.ToggleCollect());
                    if (_home.DoCollect)
                    {
                        AddBodyText(ModLocalization.T("hearthwife_menu_collect_hint"), 1);
                        AddToggle(ModLocalization.T("hearthwife_menu_pickup_berries"),
                            () => _home.IsPickupAllowed(PickupCategory.Berries),
                            () => _home.TogglePickupCategory(PickupCategory.Berries));
                        AddToggle(ModLocalization.T("hearthwife_menu_pickup_mushrooms"),
                            () => _home.IsPickupAllowed(PickupCategory.Mushrooms),
                            () => _home.TogglePickupCategory(PickupCategory.Mushrooms));
                        AddToggle(ModLocalization.T("hearthwife_menu_pickup_herbs"),
                            () => _home.IsPickupAllowed(PickupCategory.Herbs),
                            () => _home.TogglePickupCategory(PickupCategory.Herbs));
                        AddToggle(ModLocalization.T("hearthwife_menu_pickup_fish"),
                            () => _home.IsPickupAllowed(PickupCategory.Fish),
                            () => _home.TogglePickupCategory(PickupCategory.Fish));
                        AddToggle(ModLocalization.T("hearthwife_menu_pickup_materials"),
                            () => _home.IsPickupAllowed(PickupCategory.Materials),
                            () => _home.TogglePickupCategory(PickupCategory.Materials));
                        AddToggle(ModLocalization.T("hearthwife_menu_pickup_other"),
                            () => _home.IsPickupAllowed(PickupCategory.Other),
                            () => _home.TogglePickupCategory(PickupCategory.Other));
                    }

                    break;

                case Tab.Life:
                    Set(_pageTitle, ModLocalization.T("hearthwife_menu_page_life"));
                    AddStatusBlock(2);
                    // Single presence block — only ship-ready toggles, no empty section headers.
                    AddSection(ModLocalization.T("hearthwife_menu_section_presence"));
                    if (IsChoreMenuOn(PluginConfig.EnableSit))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_sit"), () => _home.DoSit, () => _home.ToggleSit());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableSitFire))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_sitfire"), () => _home.DoSitFire, () => _home.ToggleSitFire());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableNap))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_nap"), () => _home.DoNap, () => _home.ToggleNap());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableWifeComfort))
                    {
                        var bonus = PluginConfig.WifeComfortBonus != null
                            ? PluginConfig.WifeComfortBonus.Value
                            : 2;
                        AddToggle(
                            ModLocalization.T("hearthwife_menu_toggle_comfort", bonus.ToString()),
                            () => _home.DoComfort,
                            () => _home.ToggleComfort());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableMusic))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_music"), () => _home.DoMusic, () => _home.ToggleMusic());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableRested))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_rested"), () => _home.DoRested, () => _home.ToggleRested());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableAffection))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_affection"), () => _home.DoAffection, () => _home.ToggleAffection());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableWifeMapPin))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_mappin"), () => _home.DoMapPin, () => _home.ToggleMapPin());
                    }

                    if (IsChoreMenuOn(PluginConfig.EnableProtect))
                    {
                        AddToggle(ModLocalization.T("hearthwife_menu_toggle_protect"), () => _home.DoProtect, () => _home.ToggleProtect());
                    }

                    break;

                case Tab.Debug:
                    Set(_pageTitle, ModLocalization.T("hearthwife_menu_page_debug"));
                    AddStatusBlock(2);
                    AddBodyText(ModLocalization.T("hearthwife_debug_hint"), 2);

                    AddSection(ModLocalization.T("hearthwife_menu_section_recover"));
                    AddFullBtn(ModLocalization.T("hearthwife_debug_bring"), () =>
                    {
                        _home?.BringWife();
                        RefreshStatus();
                    });
                    AddFullBtn(ModLocalization.T("hearthwife_debug_skip_wait"), () =>
                    {
                        _home?.ForceWifeSkipWait();
                        RefreshStatus();
                    });

                    AddSection(ModLocalization.T("hearthwife_menu_section_force_chores"));
                    AddRow2(ModLocalization.T("hearthwife_debug_fire"), () =>
                    {
                        _home?.ForceWifeFire();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_collect"), () =>
                    {
                        _home?.ForceWifeCollect();
                        RefreshStatus();
                    });
                    AddRow2(ModLocalization.T("hearthwife_debug_repair"), () =>
                    {
                        _home?.ForceWifeRepair();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_cook_done"), () =>
                    {
                        _home?.ForceWifeCookCollect();
                        RefreshStatus();
                    });
                    AddRow2(ModLocalization.T("hearthwife_debug_cook_place"), () =>
                    {
                        _home?.ForceWifeCook();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_skip_wait"), () =>
                    {
                        _home?.ForceWifeSkipWait();
                        RefreshStatus();
                    });

                    AddSection(ModLocalization.T("hearthwife_menu_section_force_pose"));
                    AddRow2(ModLocalization.T("hearthwife_debug_sit"), () =>
                    {
                        _home?.ForceWifeSit();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_stand"), () =>
                    {
                        _home?.ForceWifeStand();
                        RefreshStatus();
                    });
                    AddRow2(ModLocalization.T("hearthwife_debug_nap"), () =>
                    {
                        _home?.ForceWifeNap();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_sitfire"), () =>
                    {
                        _home?.ForceWifeSitFire();
                        RefreshStatus();
                    });
                    AddRow2(ModLocalization.T("hearthwife_debug_sleep"), () =>
                    {
                        _home?.ForceWifeSleep();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_wake"), () =>
                    {
                        _home?.ForceWifeWake();
                        RefreshStatus();
                    });

                    AddSection(ModLocalization.T("hearthwife_menu_section_force_moments"));
                    AddRow2(ModLocalization.T("hearthwife_debug_music"), () =>
                    {
                        _home?.ForceWifeMusic();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_affection"), () =>
                    {
                        _home?.ForceWifeAffection();
                        RefreshStatus();
                    });
                    AddRow2(ModLocalization.T("hearthwife_debug_morning"), () =>
                    {
                        _home?.ForceWifeMorning();
                        RefreshStatus();
                    }, ModLocalization.T("hearthwife_debug_rested"), () =>
                    {
                        _home?.ForceWifeRested();
                        RefreshStatus();
                    });
                    break;
            }

            FinishPageLayout(
                keepScroll && sameTab ? savedPixels : 0f,
                keepScroll: keepScroll && sameTab);
        }

        private static void ClearPageWidgets()
        {
            foreach (var go in _pageWidgets)
            {
                if (go != null)
                {
                    Object.Destroy(go);
                }
            }

            _pageWidgets.Clear();
            _status = null;
        }

        private static void RefreshStatus()
        {
            if (_home == null)
            {
                return;
            }

            Set(_title, _home.HasWife
                ? _home.WifeDisplayName
                : _home.WifeDisplayName + ModLocalization.T("hearthwife_status_absent"));
            Set(_hint, ModLocalization.T("hearthwife_menu_hint"));
            if (_status != null)
            {
                _status.text = BuildStatusText();
            }
        }

        private static string BuildStatusText()
        {
            if (_home == null)
            {
                return "";
            }

            switch (_tab)
            {
                case Tab.Home:
                    return ModLocalization.T("hearthwife_status_mode", PluginConfig.LifestyleLabel(_home.Lifestyle)) +
                           "\n" + (_home.HasAssignedBed
                               ? ModLocalization.T("hearthwife_status_bed_ok")
                               : ModLocalization.T("hearthwife_status_bed_none"));
                case Tab.Look:
                    return ModLocalization.T("hearthwife_menu_label_dress") + " " +
                           (_home.DressIndex + 1) + "/" + WifeLooks.DressCount +
                           "   " + ModLocalization.T("hearthwife_menu_label_hair") + " " +
                           (_home.HairIndex + 1) + "/" + WifeLooks.HairCount +
                           "\n" + ModLocalization.T("hearthwife_menu_label_haircolor") + " " +
                           (_home.HairColorIndex + 1) + "/" + WifeLooks.HairColorCount +
                           "   " + ModLocalization.T("hearthwife_menu_label_skin") + " " +
                           (_home.SkinIndex + 1) + "/" + WifeLooks.SkinCount;
                case Tab.Work:
                    var workBits = new List<string>();
                    if (_home.DoFire)
                    {
                        workBits.Add(ModLocalization.T("hearthwife_status_bit_fire"));
                    }

                    if (_home.DoRepair)
                    {
                        workBits.Add(ModLocalization.T("hearthwife_status_bit_repair"));
                    }

                    if (_home.DoCook)
                    {
                        workBits.Add(ModLocalization.T("hearthwife_status_bit_cook"));
                    }

                    if (_home.DoCollect)
                    {
                        workBits.Add(ModLocalization.T("hearthwife_status_bit_collect"));
                    }

                    return (!PluginConfig.AllowsWork(_home.Lifestyle)
                               ? ModLocalization.T("hearthwife_status_work_paused") + "\n"
                               : ModLocalization.T("hearthwife_status_mode_line",
                                     PluginConfig.LifestyleLabel(_home.Lifestyle)) + "\n") +
                           (workBits.Count > 0
                               ? string.Join(" · ", workBits)
                               : ModLocalization.T("hearthwife_status_no_chores")) +
                           (_home.IsStorageFull
                               ? "\n" + ModLocalization.T("hearthwife_status_chest_full")
                               : "");

                case Tab.Life:
                    return (_home.DoComfort
                               ? ModLocalization.T("hearthwife_status_comfort_yes")
                               : ModLocalization.T("hearthwife_status_comfort_no")) +
                           "  ·  " +
                           (_home.DoMapPin
                               ? ModLocalization.T("hearthwife_status_map_yes")
                               : ModLocalization.T("hearthwife_status_map_no")) +
                           "\n" +
                           (_home.DoAffection
                               ? ModLocalization.T("hearthwife_status_affection_yes")
                               : ModLocalization.T("hearthwife_status_affection_no")) +
                           "  ·  " +
                           (_home.DoSit
                               ? ModLocalization.T("hearthwife_status_sit_yes")
                               : ModLocalization.T("hearthwife_status_sit_no"));
                case Tab.Debug:
                    return _home.GetWifeTestStatus();
                default:
                    return "";
            }
        }

        // --- VLG row builders (no manual Y) ---

        private static GameObject CreateRow(float preferredHeight)
        {
            var go = new GameObject("HwRow", typeof(RectTransform), typeof(LayoutElement));
            go.transform.SetParent(_scrollContent, false);
            var le = go.GetComponent<LayoutElement>();
            le.minHeight = preferredHeight;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = 1f;
            _pageWidgets.Add(go);
            return go;
        }

        private static void PrepareChildRect(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static void AddStatusBlock(int lines)
        {
            var h = 26f + lines * 17f;
            var row = CreateRow(h);
            AddRowBand(row, ColStatusBand);

            var go = GUIManager.Instance.CreateText(
                text: BuildStatusText(),
                parent: row.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: FontHint,
                color: ColStatus,
                outline: true,
                outlineColor: Color.black,
                width: ScrollInnerW - 40f,
                height: h,
                addContentSizeFitter: false);
            PrepareChildRect(go.GetComponent<RectTransform>());
            var rt = go.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(10f, 4f);
            rt.offsetMax = new Vector2(-10f, -4f);
            var t = go.GetComponent<Text>();
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            go.transform.SetAsLastSibling();
            _status = t;
        }

        private static void AddSection(string title)
        {
            // Air before a group so it doesn't glue to the previous toggle.
            CreateRow(8f);

            var row = CreateRow(34f);
            AddRowBand(row, ColSectionBand);

            var go = GUIManager.Instance.CreateText(
                text: "▸  " + title,
                parent: row.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: FontSection,
                color: ColSection,
                outline: true,
                outlineColor: Color.black,
                width: ScrollInnerW - 40f,
                height: 34f,
                addContentSizeFitter: false);
            PrepareChildRect(go.GetComponent<RectTransform>());
            var t = go.GetComponent<Text>();
            t.alignment = TextAnchor.MiddleLeft;
            // Title sits above band in hierarchy — draw text after band.
            go.transform.SetAsLastSibling();

            var rule = CreateRow(3f);
            var ruleGo = new GameObject("HwDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ruleGo.transform.SetParent(rule.transform, false);
            PrepareChildRect(ruleGo.GetComponent<RectTransform>());
            var ruleRt = ruleGo.GetComponent<RectTransform>();
            ruleRt.offsetMin = new Vector2(4f, 0f);
            ruleRt.offsetMax = new Vector2(-4f, 0f);
            var ruleImg = ruleGo.GetComponent<Image>();
            ruleImg.color = ColDivider;
            ruleImg.raycastTarget = false;
        }

        private static void AddRowBand(GameObject row, Color color)
        {
            var band = new GameObject("HwBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            band.transform.SetParent(row.transform, false);
            band.transform.SetAsFirstSibling();
            var ignore = band.GetComponent<LayoutElement>();
            ignore.ignoreLayout = true;
            PrepareChildRect(band.GetComponent<RectTransform>());
            var img = band.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        private static void AddBodyText(string text, int lines)
        {
            var h = Mathf.Max(24f, 16f + lines * 15f);
            var row = CreateRow(h);
            var go = GUIManager.Instance.CreateText(
                text: text,
                parent: row.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: FontHint,
                color: ColHint,
                outline: true,
                outlineColor: Color.black,
                width: ScrollInnerW - 40f,
                height: h,
                addContentSizeFitter: false);
            PrepareChildRect(go.GetComponent<RectTransform>());
            var rt = go.GetComponent<RectTransform>();
            rt.offsetMin = new Vector2(14f, 0f);
            rt.offsetMax = new Vector2(-8f, 0f);
            var t = go.GetComponent<Text>();
            t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static void AddFullBtn(string text, UnityEngine.Events.UnityAction action)
        {
            var row = CreateRow(BtnH + 8f);
            AddRowBand(row, ColActionBand);
            var go = GUIManager.Instance.CreateButton(
                text: text,
                parent: row.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: ScrollInnerW - 48f,
                height: BtnH);
            go.GetComponent<Button>().onClick.AddListener(action);
            go.transform.SetAsLastSibling();
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.fontSize = FontToggleBtn;
            }
        }

        private static void AddRow2(string left, UnityEngine.Events.UnityAction leftAct,
            string right, UnityEngine.Events.UnityAction rightAct)
        {
            var row = CreateRow(BtnH + 8f);
            AddRowBand(row, ColActionBand);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(8, 8, 4, 4);

            AddRowButton(row.transform, left, leftAct);
            AddRowButton(row.transform, right, rightAct);
        }

        private static void AddRowButton(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var slot = new GameObject("BtnSlot", typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            var le = slot.GetComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minHeight = BtnH;
            le.preferredHeight = BtnH;

            var go = GUIManager.Instance.CreateButton(
                text: text,
                parent: slot.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: (ScrollInnerW - 60f) * 0.5f,
                height: BtnH);
            go.GetComponent<Button>().onClick.AddListener(action);
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.fontSize = FontBody;
            }
        }

        private static void AddToggle(string label, System.Func<bool> get, System.Action toggle)
        {
            if (_home == null)
            {
                return;
            }

            var row = CreateRow(RowH + 2f);
            AddRowBand(row, ColToggleBand);

            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 10f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(10, 8, 2, 2);

            var labelSlot = new GameObject("Label", typeof(RectTransform), typeof(LayoutElement));
            labelSlot.transform.SetParent(row.transform, false);
            var labelLe = labelSlot.GetComponent<LayoutElement>();
            labelLe.flexibleWidth = 1f;
            labelLe.minWidth = 180f;
            labelLe.preferredHeight = RowH;

            var labelGo = GUIManager.Instance.CreateText(
                text: label,
                parent: labelSlot.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: FontBody,
                color: ColItemLabel,
                outline: true,
                outlineColor: Color.black,
                width: 280f,
                height: RowH,
                addContentSizeFitter: false);
            PrepareChildRect(labelGo.GetComponent<RectTransform>());
            labelGo.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;

            var btnSlot = new GameObject("ToggleBtn", typeof(RectTransform), typeof(LayoutElement));
            btnSlot.transform.SetParent(row.transform, false);
            var btnLe = btnSlot.GetComponent<LayoutElement>();
            btnLe.minWidth = 118f;
            btnLe.preferredWidth = 118f;
            btnLe.preferredHeight = BtnH;

            var on = get();
            var btn = GUIManager.Instance.CreateButton(
                text: on
                    ? "●  " + ModLocalization.T("hearthwife_on")
                    : "○  " + ModLocalization.T("hearthwife_off"),
                parent: btnSlot.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: 112f,
                height: BtnH);
            btn.GetComponent<Button>().onClick.AddListener(() =>
            {
                toggle();
                ShowTab(_tab, keepScroll: true);
            });
            foreach (var t in btn.GetComponentsInChildren<Text>(true))
            {
                t.fontSize = FontToggleBtn;
                t.color = on
                    ? new Color(0.55f, 0.9f, 0.45f, 1f)
                    : new Color(0.85f, 0.7f, 0.55f, 1f);
            }
        }

        private static void AddCycleRow(string label, string value,
            UnityEngine.Events.UnityAction back, UnityEngine.Events.UnityAction next)
        {
            var row = CreateRow(RowH + 2f);
            AddRowBand(row, ColToggleBand);
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;
            hlg.padding = new RectOffset(8, 8, 2, 2);

            AddCycleSideBtn(row.transform, "◀", back);
            var mid = new GameObject("CycleLabel", typeof(RectTransform), typeof(LayoutElement));
            mid.transform.SetParent(row.transform, false);
            var midLe = mid.GetComponent<LayoutElement>();
            midLe.flexibleWidth = 1f;
            midLe.preferredHeight = RowH;

            var midGo = GUIManager.Instance.CreateText(
                text: label + "   " + value,
                parent: mid.transform,
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                position: Vector2.zero,
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: FontBody,
                color: ColItemLabel,
                outline: true,
                outlineColor: Color.black,
                width: 280f,
                height: RowH,
                addContentSizeFitter: false);
            PrepareChildRect(midGo.GetComponent<RectTransform>());
            midGo.GetComponent<Text>().alignment = TextAnchor.MiddleCenter;

            AddCycleSideBtn(row.transform, "▶", next);
        }

        private static void AddCycleSideBtn(Transform parent, string text, UnityEngine.Events.UnityAction action)
        {
            var slot = new GameObject("CycleBtn", typeof(RectTransform), typeof(LayoutElement));
            slot.transform.SetParent(parent, false);
            var le = slot.GetComponent<LayoutElement>();
            le.minWidth = 48f;
            le.preferredWidth = 48f;
            le.preferredHeight = BtnH;

            var go = GUIManager.Instance.CreateButton(
                text: text,
                parent: slot.transform,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                position: Vector2.zero,
                width: 44f,
                height: BtnH);
            go.GetComponent<Button>().onClick.AddListener(action);
            foreach (var t in go.GetComponentsInChildren<Text>(true))
            {
                t.fontSize = FontBody;
            }
        }

        private static Text MakeFixedText(string text, float x, float y, int size, float w, float h)
        {
            var go = GUIManager.Instance.CreateText(
                text: text,
                parent: _root.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(x, y),
                font: GUIManager.Instance.AveriaSerifBold,
                fontSize: size,
                color: GUIManager.Instance.ValheimOrange,
                outline: true,
                outlineColor: Color.black,
                width: w,
                height: h,
                addContentSizeFitter: false);
            return go.GetComponent<Text>();
        }

        private static GameObject MakeFixedBtn(string text, float x, float y, float w, float h,
            UnityEngine.Events.UnityAction action)
        {
            var go = GUIManager.Instance.CreateButton(
                text: text,
                parent: _root.transform,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                position: new Vector2(x, y),
                width: w,
                height: h);
            go.GetComponent<Button>().onClick.AddListener(action);
            return go;
        }

        private static void Set(Text t, string s)
        {
            if (t != null)
            {
                t.text = s;
            }
        }

        /// <summary>Menu surface only for chores enabled in config (disabled = future upgrade).</summary>
        private static bool IsChoreMenuOn(BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            return entry == null || entry.Value;
        }
    }
}
