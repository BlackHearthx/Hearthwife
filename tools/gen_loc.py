#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Generate / verify Hearthwife localization JSON files.

Loads the English master keys from Translations/English/hearthwife.json,
loads embedded packs from tools/loc_packs.json (all managed languages),
writes Translations/<Folder>/hearthwife.json, and verifies key parity.

Usage:
  python tools/gen_loc.py              # regenerate all managed langs from packs
  python tools/gen_loc.py --check      # verify only (no write)
  python tools/gen_loc.py --sync-packs # refresh loc_packs.json from Translations/
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
EN_PATH = ROOT / "Translations" / "English" / "hearthwife.json"
PACKS_PATH = Path(__file__).resolve().parent / "loc_packs.json"

# Managed languages regenerated from tools/loc_packs.json
# (Portuguese_Brazilian is maintained separately; English is the master.)
MANAGED_LANGS = [
    "German",
    "French",
    "Spanish",
    "Italian",
    "Dutch",
    "Swedish",
    "Polish",
    "Russian",
    "Turkish",
    "Ukrainian",
    "Chinese",
    "Chinese_Trad",
    "Japanese",
    "Korean",
    "Portuguese_European",
]

# hearthwife_bind_recall must be: [<color=yellow><b>LOCALIZED_CROUCH+E</b></color>] + trailing space
BIND_RECALL = {
    "German": "[<color=yellow><b>Ducken+E</b></color>] ",
    "French": "[<color=yellow><b>Accroupi+E</b></color>] ",
    "Spanish": "[<color=yellow><b>Agachar+E</b></color>] ",
    "Italian": "[<color=yellow><b>Accovacciati+E</b></color>] ",
    "Dutch": "[<color=yellow><b>Bukken+E</b></color>] ",
    "Swedish": "[<color=yellow><b>Ducka+E</b></color>] ",
    "Polish": "[<color=yellow><b>Kucnij+E</b></color>] ",
    "Russian": "[<color=yellow><b>Присесть+E</b></color>] ",
    "Turkish": "[<color=yellow><b>Eğil+E</b></color>] ",
    "Ukrainian": "[<color=yellow><b>Присісти+E</b></color>] ",
    "Chinese": "[<color=yellow><b>蹲下+E</b></color>] ",
    "Chinese_Trad": "[<color=yellow><b>蹲下+E</b></color>] ",
    "Japanese": "[<color=yellow><b>しゃがむ+E</b></color>] ",
    "Korean": "[<color=yellow><b>앉기+E</b></color>] ",
    "Portuguese_European": "[<color=yellow><b>Agachar+E</b></color>] ",
}


def load_json(path: Path) -> dict:
    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict):
        raise SystemExit(f"Expected object in {path}")
    return data


def write_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(data, ensure_ascii=False, indent=2) + "\n"
    path.write_text(text, encoding="utf-8")


def lang_path(folder: str) -> Path:
    return ROOT / "Translations" / folder / "hearthwife.json"


def load_packs() -> dict[str, dict]:
    """Load embedded translation dicts from tools/loc_packs.json."""
    if not PACKS_PATH.is_file():
        raise SystemExit(
            f"Missing embedded packs: {PACKS_PATH}\n"
            "Run with --sync-packs after writing Translations/<Lang>/hearthwife.json"
        )
    packs = load_json(PACKS_PATH)
    for folder in MANAGED_LANGS:
        if folder not in packs:
            raise SystemExit(f"loc_packs.json missing language: {folder}")
    return packs


def ordered_like_english(en: dict, loc: dict) -> dict:
    out = {}
    for k in en:
        if k not in loc:
            raise KeyError(k)
        out[k] = loc[k]
    return out


def verify_parity(en: dict, loc: dict, label: str) -> list[str]:
    errors: list[str] = []
    en_keys = set(en)
    loc_keys = set(loc)
    missing = en_keys - loc_keys
    extra = loc_keys - en_keys
    if missing:
        errors.append(f"{label}: missing {len(missing)} keys (e.g. {sorted(missing)[:3]})")
    if extra:
        errors.append(f"{label}: extra {len(extra)} keys (e.g. {sorted(extra)[:3]})")
    if len(loc) != len(en):
        errors.append(f"{label}: key count {len(loc)} != English {len(en)}")
    for k, ev in en.items():
        if k not in loc:
            continue
        lv = loc[k]
        for ph in ("{0}", "{1}", "{2}"):
            if ph in ev and ph not in lv:
                errors.append(f"{label}: {k} missing placeholder {ph}")
    return errors


def sync_packs_from_translations(en: dict) -> None:
    """Refresh tools/loc_packs.json from current Translations/<Lang> files."""
    packs: dict[str, dict] = {}
    for folder in MANAGED_LANGS:
        path = lang_path(folder)
        if not path.is_file():
            raise SystemExit(f"Cannot sync — missing {path}")
        loc = load_json(path)
        if folder in BIND_RECALL:
            loc["hearthwife_bind_recall"] = BIND_RECALL[folder]
        errs = verify_parity(en, loc, folder)
        if errs:
            raise SystemExit("Parity errors while syncing:\n  " + "\n  ".join(errs))
        packs[folder] = ordered_like_english(en, loc)
    write_json(PACKS_PATH, packs)
    print(f"Synced embedded packs -> {PACKS_PATH}")
    for folder, loc in packs.items():
        print(f"  {folder:14} keys={len(loc)}")


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate/verify Hearthwife loc files")
    ap.add_argument(
        "--check",
        action="store_true",
        help="Verify key parity only; do not rewrite files",
    )
    ap.add_argument(
        "--sync-packs",
        action="store_true",
        help="Refresh tools/loc_packs.json from Translations/ (source of truth sync)",
    )
    args = ap.parse_args()

    if not EN_PATH.is_file():
        print(f"English master not found: {EN_PATH}", file=sys.stderr)
        return 1

    en = load_json(EN_PATH)
    print(f"English master: {EN_PATH}  keys={len(en)}")

    if args.sync_packs:
        sync_packs_from_translations(en)
        return 0

    packs = load_packs()
    all_errors: list[str] = []
    results: list[tuple[str, int]] = []

    for folder in MANAGED_LANGS:
        path = lang_path(folder)
        loc = dict(packs[folder])

        if folder in BIND_RECALL:
            loc["hearthwife_bind_recall"] = BIND_RECALL[folder]

        errs = verify_parity(en, loc, folder)
        all_errors.extend(errs)

        if not errs:
            ordered = ordered_like_english(en, loc)
            if not args.check:
                write_json(path, ordered)
                count = len(load_json(path))
            else:
                count = len(ordered)
        else:
            count = len(loc)

        results.append((folder, count))
        status = "OK" if not errs else "FAIL"
        action = "checked" if args.check else ("wrote" if not errs else "skipped write")
        print(f"  [{status}] {folder:14} keys={count:3}  {action}  -> {path}")

    print()
    print("Key counts:")
    print(f"  {'English':14} {len(en)}")
    for folder, count in results:
        print(f"  {folder:14} {count}")

    if all_errors:
        print("\nParity errors:", file=sys.stderr)
        for e in all_errors:
            print(f"  - {e}", file=sys.stderr)
        return 1

    print("\nAll managed languages match English key set.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
