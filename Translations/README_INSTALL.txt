# If this file exists next to Hearthwife.dll after install, the Thunderstore/GitHub
# package included the Translations folder correctly.
#
# Expected siblings:
#   Hearthwife.dll
#   Translations/
#     English/hearthwife.json
#     Portuguese_Brazilian/hearthwife.json
#     ... (17 language folders total)
#
# r2modman path (example):
#   .../BepInEx/plugins/BlackHearthx-Hearthwife/Translations/English/hearthwife.json
#
# In-game log on success (BepInEx/LogOutput.log):
#   [Info:Hearthwife.ModLocalization] Hearthwife: loaded localization for 17 language(s) (disk=17, embedded=0)
# or if folder missing but DLL embed works:
#   (disk=0, embedded=17)
