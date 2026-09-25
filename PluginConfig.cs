using BepInEx.Configuration;

namespace Hearthwife
{
    internal static class PluginConfig
    {
        internal static ConfigEntry<float> HomeRadius;
        internal static ConfigEntry<bool> ShowHomeArea;
        /// <summary>Hard cap on placed wife idols (and thus wives). Default 1.</summary>
        internal static ConfigEntry<int> MaxIdols;
        /// <summary>Show Debug tab in wife menu. Keep ON while building; ship OFF later.</summary>
        internal static ConfigEntry<bool> ShowDebugTab;
        internal static ConfigEntry<bool> IdleWander;
        internal static ConfigEntry<float> ThinkInterval;
        internal static ConfigEntry<float> ChoreCooldown;
        internal static ConfigEntry<float> MoveSpeed;
        internal static ConfigEntry<bool> ParkNearIdol;
        /// <summary>When true: park near idol only — legacy; prefer LifestyleMode.Leisure.</summary>
        internal static ConfigEntry<bool> BasicsOnly;
        /// <summary>0 Leisure, 1 Balanced, 2 Diligent — default for new idols.</summary>
        internal static ConfigEntry<int> DefaultLifestyle;
        internal static ConfigEntry<bool> EnableRepair;
        internal static ConfigEntry<bool> EnableCollect;
        internal static ConfigEntry<bool> EnableFish;
        internal static ConfigEntry<bool> EnableCook;
        internal static ConfigEntry<bool> EnableFire;
        /// <summary>Min fuel stacks left in base chests (not idol) when she takes wood for fires.</summary>
        internal static ConfigEntry<int> FireWoodReserve;
        internal static ConfigEntry<bool> EnableMead;
        internal static ConfigEntry<bool> EnableSmelt;
        internal static ConfigEntry<bool> EnableFarm;
        internal static ConfigEntry<bool> EnableForage;
        internal static ConfigEntry<bool> EnableGarden;
        internal static ConfigEntry<bool> EnableEat;
        internal static ConfigEntry<bool> EnableSit;
        internal static ConfigEntry<bool> EnableNap;
        internal static ConfigEntry<bool> EnableSitFire;
        internal static ConfigEntry<bool> EnableAmbientGreet;
        internal static ConfigEntry<bool> EnableProtect;
        internal static ConfigEntry<bool> EnableAffection;
        internal static ConfigEntry<bool> EnableMusic;
        internal static ConfigEntry<bool> EnableCauldron;
        internal static ConfigEntry<bool> EnableReplant;
        internal static ConfigEntry<bool> EnableOpenDoors;
        internal static ConfigEntry<bool> EnableRested;
        internal static ConfigEntry<bool> EnableWifeComfort;
        internal static ConfigEntry<int> WifeComfortBonus;
        internal static ConfigEntry<bool> EnableWifeMapPin;
        internal static ConfigEntry<bool> UseCustomIdolMesh;
        internal static ConfigEntry<bool> EnableAtmosphere;
        internal static ConfigEntry<bool> EnableDayNight;

        internal static void Bind(ConfigFile config)
        {
            HomeRadius = config.Bind("Wife", "HomeRadius", 32f,
                "Raio do lar em metros. Ela trabalha e passeia dentro desse círculo.");
            MaxIdols = config.Bind("Wife", "MaxIdols", 1,
                "Máximo de ídolos (e esposas) no mundo. 1 = uma esposa (recomendado).");
            ShowDebugTab = config.Bind("Wife", "ShowDebugTab", false,
                "Aba de testes no menu (só para desenvolvimento). Deixe desligado no jogo normal.");
            ShowHomeArea = config.Bind("Wife", "ShowHomeArea", true,
                "Mostra o círculo azul perto do ídolo.");
            BasicsOnly = config.Bind("Wife", "BasicsOnly", true,
                "Legado de configs antigas. Prefira o modo de vida no menu do ídolo.");
            DefaultLifestyle = config.Bind("Wife", "LifestyleMode", 1,
                "Modo padrão de ídolos novos: 0 = Vida no lar, 1 = Misto (padrão), 2 = Trabalhadora.");
            // Idol = spawn/chest/menu only. She lives in the home radius, not glued to it.
            ParkNearIdol = config.Bind("Wife", "CampAtTotem", false,
                "Desligado (recomendado): ela vive na área da casa. Ligado = fica na frente do ídolo.");
            IdleWander = config.Bind("Wife", "ShortStroll", true,
                "Andar pela área do lar (círculo do ídolo).");
            ThinkInterval = config.Bind("Wife", "ThinkIntervalV4", 2.8f,
                "Segundos entre decisões de passeio/presença. Mais baixo = ela reage mais cedo após carregar o mundo.");
            ChoreCooldown = config.Bind("Wife", "ChoreCooldownV2", 18f,
                "Após terminar uma tarefa, espera estes segundos antes de pegar outra.");
            MoveSpeed = config.Bind("Wife", "MoveSpeed", 2.4f,
                "Velocidade ao andar.");
            EnableAmbientGreet = config.Bind("Wife", "AmbientGreet", true,
                "Ela te cumprimenta quando você chega perto.");
            EnableProtect = config.Bind("Wife", "ProtectWarn", false,
                "Avisa se alguma peça do lar foi destruída ou está muito danificada.");
            EnableAffection = config.Bind("Wife", "Affection", true,
                "Carinho quando você fica parado perto dela.");
            EnableMusic = config.Bind("Wife", "MusicIdle", false,
                "Dança ou canta de vez em quando.");

            EnableRepair = config.Bind("Chores", "EnableRepairV2", true,
                "Reparar peças danificadas no círculo do lar (ligado por padrão).");
            EnableCollect = config.Bind("Chores", "EnableCollectV2", true,
                "Recolher no lar: arbustos e itens no chão vão para o baú do ídolo (ligado por padrão).");
            EnableFish = config.Bind("Chores", "EnableFish", false, "Permitir pesca (isca no baú).");
            EnableCook = config.Bind("Chores", "EnableCookV2", true,
                "Cozinhar no fogão/grelha (ligado por padrão). Usa comida do baú do ídolo.");
            EnableCauldron = config.Bind("Chores", "EnableCauldron", false, "Permitir craftar no caldeirão.");
            EnableFire = config.Bind("Chores", "EnableFireV2", true,
                "Manter lareira, fogueira e tocha acesas (ligado por padrão).");
            FireWoodReserve = config.Bind("Chores", "FireWoodReserve", 20,
                "Lenha do lar: em baús da base (não o do ídolo), ela deixa pelo menos N de cada combustível. O baú do ídolo não guarda reserva.");
            EnableMead = config.Bind("Chores", "EnableMead", false, "Permitir hidromel.");
            EnableSmelt = config.Bind("Chores", "EnableSmelt", false, "Permitir forno / fundição.");
            EnableFarm = config.Bind("Chores", "EnableFarm", false, "Permitir colher plantações.");
            EnableForage = config.Bind("Chores", "EnableForageV2", true,
                "Inclui colher arbustos dentro de Recolher.");
            EnableReplant = config.Bind("Chores", "EnableReplant", false, "Replantar após colheita.");
            EnableGarden = config.Bind("Chores", "EnableGarden", false, "Avisar jardim / coletar mel.");
            EnableEat = config.Bind("Chores", "EnableEat", false, "Comer do baú se a vida estiver baixa.");
            EnableOpenDoors = config.Bind("Chores", "OpenDoors", true, "Abrir portas quando ela ficar presa.");

            EnableSit = config.Bind("Idle", "SitChairs", true, "Sentar em cadeiras dentro do círculo.");
            EnableNap = config.Bind("Idle", "EnableNap", false, "Cochilar na cama de dia.");
            EnableSitFire = config.Bind("Idle", "EnableSitFire", false, "Sentar junto à fogueira.");
            EnableRested = config.Bind("Idle", "RestedNearFire", false,
                "Buff Descansado perto do fogo — também no menu Vida.");
            EnableWifeComfort = config.Bind("Life", "WifeHomeComfort", true,
                "Com ela no lar e você dentro do círculo, aumenta o conforto.");
            WifeComfortBonus = config.Bind("Life", "WifeHomeComfortBonus", 2,
                "Pontos de conforto extras enquanto ela está no lar com você (padrão 2).");
            EnableWifeMapPin = config.Bind("Life", "WifeMapPin", true,
                "Mostra ela no mapa com um coração e o nome dela.");
            EnableAtmosphere = config.Bind("Life", "WeatherMood", true,
                "Reage ao clima (chuva, neblina, sol, frio…) com fala e busca abrigo.");
            EnableDayNight = config.Bind("Life", "DayNightSleep", true,
                "De noite vai dormir na cama (se definida); de dia acorda.");
            UseCustomIdolMesh = config.Bind("Idol", "UseConceptTotem", true,
                "Usar o visual do ídolo do conceito. Deixe ligado.");
        }

        /// <summary>Legacy alias — Leisure mode (no work chores).</summary>
        internal static bool IsBasicsOnly =>
            ResolveDefaultLifestyle() == LifestyleMode.Leisure;

        internal static LifestyleMode ResolveDefaultLifestyle()
        {
            if (DefaultLifestyle != null)
            {
                return ClampLifestyle(DefaultLifestyle.Value);
            }

            // Old cfg: BasicsOnly true → Leisure, false → Balanced.
            if (BasicsOnly != null && !BasicsOnly.Value)
            {
                return LifestyleMode.Balanced;
            }

            // Missing bind / legacy: prefer Balanced so chores work out of the box.
            return LifestyleMode.Balanced;
        }

        internal static LifestyleMode ClampLifestyle(int raw)
        {
            if (raw < 0)
            {
                return LifestyleMode.Leisure;
            }

            if (raw > 2)
            {
                return LifestyleMode.Diligent;
            }

            return (LifestyleMode)raw;
        }

        internal static bool AllowsWork(LifestyleMode mode) =>
            mode == LifestyleMode.Balanced || mode == LifestyleMode.Diligent;

        internal static string LifestyleLabel(LifestyleMode mode)
        {
            switch (mode)
            {
                case LifestyleMode.Balanced:
                    return ModLocalization.T("hearthwife_life_balanced");
                case LifestyleMode.Diligent:
                    return ModLocalization.T("hearthwife_life_diligent");
                default:
                    return ModLocalization.T("hearthwife_life_leisure");
            }
        }

        /// <summary>Legacy alias — prefer LifestyleLabel.</summary>
        internal static string LifestyleLabelPt(LifestyleMode mode) => LifestyleLabel(mode);

        internal static string LifestyleHint(LifestyleMode mode)
        {
            switch (mode)
            {
                case LifestyleMode.Balanced:
                    return ModLocalization.T("hearthwife_life_hint_balanced");
                case LifestyleMode.Diligent:
                    return ModLocalization.T("hearthwife_life_hint_diligent");
                default:
                    return ModLocalization.T("hearthwife_life_hint_leisure");
            }
        }

        /// <summary>Legacy alias — prefer LifestyleHint.</summary>
        internal static string LifestyleHintPt(LifestyleMode mode) => LifestyleHint(mode);
    }
}
