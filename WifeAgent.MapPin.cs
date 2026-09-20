using System.IO;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Hearthwife
{
    /// <summary>
    /// Live minimap/map pin for the wife.
    /// Vanilla UpdatePlayerPins pattern: save:false + m_pos every frame + m_pinUpdateRequired
    /// (without the flag, UpdatePins never runs while the player stands still — pin freezes).
    /// Heart icon + UI scale &lt; vanilla so it does not cover the map.
    /// </summary>
    public partial class WifeAgent
    {
        private Minimap.PinData _mapPin;
        private Minimap _mapPinMinimap;
        private float _mapPinNameAt;
        private string _mapPinName = "";
        private static Sprite _wifeHeartSprite;

        /// <summary>PinAssistant / vanilla companions: force Minimap.UpdatePins after m_pos change.</summary>
        private static AccessTools.FieldRef<Minimap, bool> s_pinUpdateRequired;

        /// <summary>Fraction of vanilla pin UI size (32 small / 48 large). ~0.85 reads clearly without covering the map.</summary>
        private const float WifePinUiScale = 0.85f;

        private static void EnsurePinUpdateField()
        {
            if (s_pinUpdateRequired != null)
            {
                return;
            }

            try
            {
                s_pinUpdateRequired = AccessTools.FieldRefAccess<Minimap, bool>("m_pinUpdateRequired");
            }
            catch
            {
                s_pinUpdateRequired = null;
            }
        }

        private static void RequestPinUiRefresh(Minimap map)
        {
            if (map == null)
            {
                return;
            }

            EnsurePinUpdateField();
            if (s_pinUpdateRequired != null)
            {
                s_pinUpdateRequired(map) = true;
            }
        }

        private void TickMapPin()
        {
            if (!_booted || _home == null)
            {
                return;
            }

            var enabled = _home.DoMapPin &&
                          (PluginConfig.EnableWifeMapPin == null || PluginConfig.EnableWifeMapPin.Value);
            if (!enabled)
            {
                ClearMapPin();
                return;
            }

            var map = Minimap.instance;
            if (map == null)
            {
                _mapPin = null;
                _mapPinMinimap = null;
                return;
            }

            if (map != _mapPinMinimap)
            {
                _mapPin = null;
                _mapPinMinimap = map;
            }

            var name = _home.WifeDisplayName;
            if (string.IsNullOrEmpty(name))
            {
                name = Localization.instance.Localize("$hearthwife_wife_name");
            }

            if (_mapPin == null)
            {
                try
                {
                    // Icon3 = neutral base type (filter-safe). Sprite overridden to heart.
                    _mapPin = map.AddPin(
                        transform.position,
                        Minimap.PinType.Icon3,
                        name,
                        save: false,
                        isChecked: false,
                        0L);
                    _mapPin.m_doubleSize = false;
                    ApplyWifePinIcon(_mapPin);
                    ApplyWifePinUiSize(_mapPin, map);
                    RequestPinUiRefresh(map);
                    _mapPinName = name;
                    _mapPinNameAt = Time.time;
                }
                catch
                {
                    _mapPin = null;
                }

                return;
            }

            // Always sync world pos + request UI refresh (vanilla player pins set m_pinUpdateRequired
            // on move; without it UpdatePins skips and the marker freezes while you stand still).
            var pos = transform.position;
            if (_mapPin.m_pos != pos)
            {
                _mapPin.m_pos = pos;
            }

            RequestPinUiRefresh(map);
            ApplyWifePinIcon(_mapPin);

            if (Time.time >= _mapPinNameAt + 2f && _mapPinName != name)
            {
                _mapPin.m_name = name;
                _mapPinName = name;
                _mapPinNameAt = Time.time;
                RequestPinUiRefresh(map);
            }
        }

        /// <summary>
        /// After Minimap.UpdatePins may recreate/reset the marker — re-apply icon + small UI size.
        /// Called from LateUpdate so we win script-order races with Minimap.
        /// </summary>
        private void LateTickMapPinUi()
        {
            if (_mapPin == null)
            {
                return;
            }

            var map = Minimap.instance;
            if (map == null)
            {
                return;
            }

            ApplyWifePinIcon(_mapPin);
            ApplyWifePinUiSize(_mapPin, map);
        }

        private static void ApplyWifePinIcon(Minimap.PinData pin)
        {
            if (pin == null)
            {
                return;
            }

            var heart = GetOrCreateHeartSprite();
            if (heart == null)
            {
                return;
            }

            pin.m_doubleSize = false;
            pin.m_icon = heart;
            if (pin.m_iconElement != null)
            {
                pin.m_iconElement.sprite = heart;
                pin.m_iconElement.color = Color.white;
            }
        }

        private static void ApplyWifePinUiSize(Minimap.PinData pin, Minimap map)
        {
            if (pin?.m_uiElement == null || map == null)
            {
                return;
            }

            var baseSize = map.m_mode == Minimap.MapMode.Large
                ? map.m_pinSizeLarge
                : map.m_pinSizeSmall;
            var size = baseSize * WifePinUiScale;
            pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size);
            pin.m_uiElement.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size);
        }

        private static Sprite GetOrCreateHeartSprite()
        {
            if (_wifeHeartSprite != null)
            {
                return _wifeHeartSprite;
            }

            var tex = LoadEmbeddedHeartTexture() ?? BuildFallbackHeartTexture(32);
            if (tex == null)
            {
                return null;
            }

            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.name = "Hearthwife_MapHeart";
            _wifeHeartSprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);
            _wifeHeartSprite.name = "Hearthwife_MapHeart";
            return _wifeHeartSprite;
        }

        private static Texture2D LoadEmbeddedHeartTexture()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string resource = null;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("hearthwife_map_heart.rgba", System.StringComparison.OrdinalIgnoreCase))
                    {
                        resource = name;
                        break;
                    }
                }

                if (resource == null)
                {
                    return null;
                }

                byte[] bytes;
                using (var stream = asm.GetManifestResourceStream(resource))
                {
                    if (stream == null)
                    {
                        return null;
                    }

                    using (var ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        bytes = ms.ToArray();
                    }
                }

                if (bytes == null || bytes.Length < 16)
                {
                    return null;
                }

                var w = System.BitConverter.ToInt32(bytes, 0);
                var h = System.BitConverter.ToInt32(bytes, 4);
                if (w <= 0 || h <= 0 || w > 256 || h > 256 || bytes.Length < 8 + w * h * 4)
                {
                    return null;
                }

                var pixels = new byte[w * h * 4];
                System.Buffer.BlockCopy(bytes, 8, pixels, 0, pixels.Length);
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, false);
                tex.LoadRawTextureData(pixels);
                tex.Apply(false, true);
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private static Texture2D BuildFallbackHeartTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var fill = new Color(0.91f, 0.35f, 0.48f, 1f);
            var clear = new Color(0f, 0f, 0f, 0f);
            // SetPixel is bottom-up friendly: y=0 is bottom; heart point down in math → flip py so tip points up on map.
            const float scale = 0.65f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var px = ((x + 0.5f) / size * 2f - 1f) / scale;
                    var py = ((y + 0.5f) / size * 2f - 1f) / scale;
                    var x2 = px * px;
                    var y2 = py * py;
                    var a = x2 + y2 - 1f;
                    var inside = a * a * a - x2 * y2 * py <= 0f;
                    tex.SetPixel(x, y, inside ? fill : clear);
                }
            }

            tex.Apply(false, true);
            return tex;
        }

        private void ClearMapPin()
        {
            if (_mapPin != null)
            {
                try
                {
                    if (Minimap.instance != null)
                    {
                        Minimap.instance.RemovePin(_mapPin);
                        RequestPinUiRefresh(Minimap.instance);
                    }
                }
                catch
                {
                }
            }

            _mapPin = null;
            _mapPinMinimap = null;
            _mapPinName = "";
        }

        private void OnDestroy()
        {
            ClearMapPin();
        }
    }
}
