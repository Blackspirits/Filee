"""Build review-only theme.json examples from pinned sources; check foreground contrast."""

import hashlib
import json
from pathlib import Path
import re
import urllib.request

folder = Path(__file__).resolve().parent
root = folder.parents[1]
cache = root / "artifacts/theme-proposal/sources"
cache.mkdir(parents=True, exist_ok=True)
records = json.loads((folder / "sources.json").read_text(encoding="utf-8"))

def source(name):
    record = next(item for item in records if item["id"] == name)["palette"]
    path = root / record["local"]
    if not path.exists():
        request = urllib.request.Request(record["url"], headers={"User-Agent": "Filee-theme-proposal"})
        with urllib.request.urlopen(request, timeout=40) as response:
            path.write_bytes(response.read())
    data = path.read_bytes()
    assert hashlib.sha256(data).hexdigest() == record["sha256"], name
    return data.decode("utf-8")

cat = json.loads(source("catppuccin"))
dracula_source = source("dracula")
monokai_source = source("monokai")
adjustments = []

def rgb(color):
    return tuple(int(color[i:i + 2], 16) for i in (1, 3, 5))

def luminance(color):
    def linear(value):
        value /= 255
        return value / 12.92 if value <= 0.04045 else ((value + 0.055) / 1.055) ** 2.4
    values = tuple(map(linear, rgb(color)))
    return sum(value * weight for value, weight in zip(values, (0.2126, 0.7152, 0.0722)))

def contrast(a, b):
    light, dark = sorted((luminance(a), luminance(b)), reverse=True)
    return (light + 0.05) / (dark + 0.05)

def adapt(name, mode, colors):
    fills = [colors[key] for key in ("page", "surface", "surfaceAlt")]
    for role in ("text", "textSecondary", "danger", "success", "warning"):
        original = colors[role]
        target = "#FFFFFF" if mode == "dark" else "#000000"
        a, b = rgb(original), rgb(target)
        for step in range(101):
            mix = step / 100
            color = "#" + "".join(f"{round(x * (1 - mix) + y * mix):02X}" for x, y in zip(a, b))
            if min(contrast(color, fill) for fill in fills) >= 4.5:
                break
        else:
            raise ValueError((name, mode, role, "cannot reach contrast target"))
        colors[role] = color
        if original.upper() != color:
            adjustments.append({"palette": name, "mode": mode, "role": role, "source": original.upper(), "adapted": color})
    return colors

def c(flavor, name):
    return cat[flavor]["colors"][name]["hex"].upper()

def cat_colors(flavor):
    light = flavor == "latte"
    return dict(page=c(flavor, "mantle"), surface=c(flavor, "base"),
                surfaceAlt=c(flavor, "crust" if light else "surface0"),
                border=c(flavor, "overlay0" if light else "surface1"),
                text=c(flavor, "text"), textSecondary=c(flavor, "subtext1"),
                danger=c(flavor, "red"), success=c(flavor, "green"), warning=c(flavor, "yellow"))

# Official Nord palette; roles follow nordtheme.com/docs/colors-and-palettes/.
nord = ["#2E3440", "#3B4252", "#434C5E", "#4C566A", "#D8DEE9", "#E5E9F0", "#ECEFF4",
        "#8FBCBB", "#88C0D0", "#81A1C1", "#5E81AC", "#BF616A", "#D08770", "#EBCB8B", "#A3BE8C", "#B48EAD"]
nord_source = source("nord")
assert all(color.upper() in nord_source.upper() for color in nord)

def nord_colors(light):
    return dict(page=nord[6 if light else 0], surface=nord[5 if light else 1],
                surfaceAlt=nord[4 if light else 2], border=nord[3],
                text=nord[0 if light else 6], textSecondary=nord[3 if light else 4],
                danger=nord[11], success=nord[14], warning=nord[13])

def dracula_colors(light):
    colors = dict(page="#FFFBEB", surface="#EFEDDC", surfaceAlt="#ECE9DF", border="#CFCFDE",
                  text="#1F1F1F", textSecondary="#6C664B") if light else dict(
        page="#21222C", surface="#282A36", surfaceAlt="#343746", border="#6272A4",
        text="#F8F8F2", textSecondary="#6272A4")
    # The specification gives separate functional colours for application UI.
    colors.update(danger="#DE5735", success="#089108", warning="#A39514")
    assert all(color.upper() in dracula_source.upper() for color in colors.values())
    return colors

monokai = dict(page="#1E1F1C", surface="#272822", surfaceAlt="#414339", border="#75715E",
               text="#F8F8F2", textSecondary="#90908A", danger="#F92672", success="#A6E22E", warning="#E6DB74")
assert all(color.upper() in monokai_source.upper() for color in monokai.values())

themes = {
    "catppuccin": {"light": cat_colors("latte"), "dark": cat_colors("mocha")},
    "nord": {"light": nord_colors(True), "dark": nord_colors(False)},
    "dracula": {"light": dracula_colors(True), "dark": dracula_colors(False)},
    "monokai": {"dark": monokai},
}

output = folder / "examples"
output.mkdir(exist_ok=True)
ratios = []
for name, variants in themes.items():
    for mode, colors in variants.items():
        adapt(name, mode, colors)
        assert len(colors) == 9
        assert all(re.fullmatch(r"#[0-9A-F]{6}", value) for value in colors.values())
        for role in ("text", "textSecondary", "danger", "success", "warning"):
            for fill in ("page", "surface", "surfaceAlt"):
                value = contrast(colors[role], colors[fill])
                assert value >= 4.5
                ratios.append({"palette": name, "mode": mode, "foreground": role, "background": fill, "ratio": round(value, 4)})
    path = output / (name + ".json")
    path.write_text(json.dumps(variants, indent=2) + "\n", encoding="utf-8")
    assert path.stat().st_size < 64 * 1024

report = {"checks": len(ratios), "minimum": min(row["ratio"] for row in ratios), "adjustments": adjustments, "ratios": ratios}
(folder / "contrast.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
print(json.dumps({"files": len(themes), "variants": sum(map(len, themes.values())), "contrast_checks": len(ratios), "minimum": report["minimum"], "adaptations": len(adjustments)}))
