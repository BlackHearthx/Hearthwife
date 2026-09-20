using BepInEx;
using HarmonyLib;
using Jotunn;
using Jotunn.Managers;
using Jotunn.Utils;

namespace Hearthwife
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.None)]
    public class HearthwifePlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.blackhearthx.hearthwife";
        public const string PluginName = "Hearthwife";
        public const string PluginVersion = "1.0.0";

        internal static HearthwifePlugin Instance { get; private set; }

        private void Awake()
        {
            Instance = this;
            PluginConfig.Bind(Config);
            ModLocalization.Register();

            PrefabManager.OnVanillaPrefabsAvailable += OnVanillaPrefabsAvailable;
            GUIManager.OnCustomGUIAvailable += OnCustomGuiAvailable;

            try
            {
                var harmony = new Harmony(PluginGUID);
                WifePatches.Apply(harmony);
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError("Hearthwife: Harmony patch failed (idol still registers): " + ex);
            }

            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} loaded");
        }

        private void Update()
        {
            WifeMenu.TickCloseKeys();
        }

        private void OnCustomGuiAvailable()
        {
            WifeMenu.Invalidate();
        }

        private void OnVanillaPrefabsAvailable()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= OnVanillaPrefabsAvailable;

            try
            {
                WifeIdol.Register();
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError("Hearthwife: WifeIdol.Register failed: " + ex);
            }

            try
            {
                WifeNpcPrefab.Register();
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError("Hearthwife: WifeNpcPrefab.Register failed: " + ex);
            }
        }
    }
}
