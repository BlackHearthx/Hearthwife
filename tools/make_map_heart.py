"""Build Assets/hearthwife_map_heart.png from OpenMoji (CC BY-SA 4.0), padded for Valheim pins."""
from __future__ import annotations

import os
import urllib.request

from PIL import Image

ASSETS = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets")
OUT = os.path.join(ASSETS, "hearthwife_map_heart.png")
SIZE = 64
URL = "https://raw.githubusercontent.com/hfg-gmuend/openmoji/master/color/72x72/2764.png"


def main() -> None:
    os.makedirs(ASSETS, exist_ok=True)
    dl = os.path.join(ASSETS, "_openmoji_heart.png")
    urllib.request.urlretrieve(URL, dl)
    im = Image.open(dl).convert("RGBA")
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    # OpenMoji already pads; ~78% of texture keeps vanilla-like visual weight.
    target = int(SIZE * 0.72)
    im = im.resize((target, target), Image.Resampling.LANCZOS)
    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = px[x, y]
            if a < 8:
                continue
            # Soft rose — homestead wife, not neon emoji red
            nr = int(r * 0.40 + 0xE8 * 0.60)
            ng = int(g * 0.40 + 0x5A * 0.60)
            nb = int(b * 0.40 + 0x7A * 0.60)
            px[x, y] = (nr, ng, nb, a)
    ox = (SIZE - target) // 2
    oy = (SIZE - target) // 2
    canvas.paste(im, (ox, oy), im)
    canvas.save(OUT, "PNG")  # upright preview
    # Embedded runtime: Unity LoadRawTextureData is bottom-up — flip V for correct map orientation
    import struct

    rgba_path = os.path.join(ASSETS, "hearthwife_map_heart.rgba")
    raw = canvas.transpose(Image.Transpose.FLIP_TOP_BOTTOM).tobytes()
    with open(rgba_path, "wb") as f:
        f.write(struct.pack("<ii", SIZE, SIZE))
        f.write(raw)
    os.remove(dl)
    with open(os.path.join(ASSETS, "ICON_ATTRIBUTION.txt"), "w", encoding="utf-8") as f:
        f.write("hearthwife_map_heart.png/.rgba derived from OpenMoji 2764 (heart)\n")
        f.write("https://openmoji.org/ — CC BY-SA 4.0\n")
        f.write("Tinted/resized for Hearthwife map pin.\n")
    print("wrote", OUT, os.path.getsize(OUT), "bytes")
    print("wrote", rgba_path, os.path.getsize(rgba_path), "bytes")


if __name__ == "__main__":
    main()
