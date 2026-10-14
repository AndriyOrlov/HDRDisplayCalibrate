"""Draws docs/banner.png and docs/social-preview.png (1280×640) from the app icon and screenshots.

Everything that matters stays inside GitHub's safe area (80 px from every edge), so the social card is never cropped.

    python tools/make_banner.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
W, H, SAFE = 1280, 640, 80


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def glow(img, xy, radius, color):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    x, y = xy
    ImageDraw.Draw(layer).ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)
    return Image.alpha_composite(img, layer.filter(ImageFilter.GaussianBlur(radius * 0.55)))


def rounded(im, r):
    mask = Image.new("L", im.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, im.size[0] - 1, im.size[1] - 1), r, fill=255)
    out = im.convert("RGBA"); out.putalpha(mask)
    return out


def shadowed(base, im, pos, r=14):
    sh = Image.new("RGBA", base.size, (0, 0, 0, 0))
    ImageDraw.Draw(sh).rounded_rectangle((pos[0] + 6, pos[1] + 12, pos[0] + im.size[0] + 6, pos[1] + im.size[1] + 12), r, fill=(0, 0, 0, 150))
    base = Image.alpha_composite(base, sh.filter(ImageFilter.GaussianBlur(14)))
    base.alpha_composite(rounded(im, r), pos)
    return base


img = Image.new("RGBA", (W, H), (22, 22, 26, 255))
# HDR-ish light: warm highlight behind the title, magenta and cyan behind the screenshots (decoration may bleed)
img = glow(img, (220, 160), 330, (255, 170, 60, 70))
img = glow(img, (960, 250), 300, (200, 60, 255, 55))
img = glow(img, (1100, 500), 260, (40, 200, 255, 55))

# right, inside the safe area: gamut page with the 3D gamut solid in front
win = Image.open(os.path.join(DOCS, "gamut.png")).convert("RGBA")
win = win.crop((300, 300, 1380, 1130)).resize((500, 384), Image.LANCZOS)
img = shadowed(img, win, (W - SAFE - 500, SAFE + 6))
g3d = Image.open(os.path.join(DOCS, "gamut3d.png")).convert("RGBA").crop((170, 20, 830, 450))
g3d = g3d.resize((int(g3d.size[0] * 0.52), int(g3d.size[1] * 0.52)), Image.LANCZOS)
card = Image.new("RGBA", (g3d.size[0] + 24, g3d.size[1] + 24), (32, 33, 36, 255))
card.alpha_composite(g3d, (12, 12))
img = shadowed(img, card, (620, H - SAFE - card.size[1]), 16)

d = ImageDraw.Draw(img)
icon = Image.open(os.path.join(ROOT, "src", "app.ico"))
icon.size = max(icon.ico.sizes())
img.alpha_composite(icon.convert("RGBA").resize((76, 76), Image.LANCZOS), (SAFE + 4, SAFE + 6))

d.text((SAFE, 168), "HDRDisplay", font=font("seguibl.ttf", 64), fill=(255, 255, 255))
d.text((SAFE, 236), "Calibrate", font=font("seguibl.ttf", 64), fill=(96, 205, 255))
body = font("segoeui.ttf", 26)
d.text((SAFE + 2, 340), "SDR + HDR calibration for Windows", font=body, fill=(215, 215, 220))
d.text((SAFE + 2, 377), "with an ordinary colorimeter.", font=body, fill=(215, 215, 220))
d.text((SAFE + 2, 424), "Native MHC2 · ΔE · gamut · 3D LUT", font=body, fill=(255, 200, 61))
d.text((SAFE + 2, 520), "github.com/AndriyOrlov/HDRDisplayCalibrate", font=font("segoeui.ttf", 19), fill=(150, 150, 155))

out = img.convert("RGB")
for name in ("banner.png", "social-preview.png"):
    out.save(os.path.join(DOCS, name), optimize=True)
print("docs/banner.png, docs/social-preview.png")
