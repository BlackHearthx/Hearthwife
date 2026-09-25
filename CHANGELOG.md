# Changelog

## 1.0.3

- Sync plugin version everywhere (DLL was still reporting 1.0.1)
- Default lifestyle is Balanced (chores on) for new idols / missing cfg
- Cooking: `RPC_AddItem(string, bool cheated)` matches Valheim 1.0 (was missing `cheated`)
- Fermenter / Smelter: claim ownership + correct `RPC_AddItem(int, bool)` / `RPC_AddOre(string, bool)` with failure logs

## 1.0.2

- Fix idol placement: hammer preview/ghost no longer counts toward MaxIdols (was blocking place with `$hearthwife_idol_limit` on a fresh world)
- Fix cooking: wife can place food on the rack again (`RPC_AddItem` used the wrong args; fire check also samples below tall racks)
- Fix localization packaging: Thunderstore zip now verifies `Translations/{Language}/hearthwife.json` and uses forward-slash zip paths (r2modman-safe)
- Embed all language JSON files in the DLL so button/menu text still loads if the Translations folder is missing after install

## 1.0.1

- Fix Thunderstore README images (use GitHub raw URLs — relative paths break on the site)

## 1.0.0

- First Thunderstore release
- Homestead wife from a furniture idol: lifestyle modes, chores (fire, gather, cook, repair), presence, map heart pin
- Menu and dialogue follow Valheim language (EN, PT-BR, DE, FR, ES, and more)
- Stall recovery / ward leash fixes from 0.9.x polishing
