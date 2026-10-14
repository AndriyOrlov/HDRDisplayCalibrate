"""Draws docs/banner.png (README hero and GitHub social preview, 1280×640) from the app icon and screenshots.

    python tools/make_banner.py
"""
import os
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DOCS = os.path.join(ROOT, "docs")
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
W, H = 1280, 640


def font(name, size):
    return ImageFont.truetype(os.path.join(FONTS, name), size)


def glow(img, xy, radius, color):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    x, y = xy
    d.ellipse((x - radius, y - radius, x + radius, y + radius), fill=color)
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
# HDR-ish light: warm highlight top-left, magenta and cyan behind the screenshots
img = glow(img, (180, 120), 330, (255, 170, 60, 70))
img = glow(img, (980, 260), 300, (200, 60, 255, 55))
img = glow(img, (1120, 520), 260, (40, 200, 255, 55))

# right: app window (gamut page) with the 3D gamut solid in front of it
win = Image.open(os.path.join(DOCS, "gamut.png")).convert("RGBA")
win = win.crop((300, 300, 1380, 1130)).resize((560, 430), Image.LANCZOS)
img = shadowed(img, win, (680, 60))
g3d = Image.open(os.path.join(DOCS, "gamut3d.png")).convert("RGBA").crop((170, 20, 830, 450))
g3d = g3d.resize((int(g3d.size[0] * 0.62), int(g3d.size[1] * 0.62)), Image.LANCZOS)
card = Image.new("RGBA", (g3d.size[0] + 24, g3d.size[1] + 24), (32, 33, 36, 255))
card.alpha_composite(g3d, (12, 12))
img = shadowed(img, card, (610, 318), 16)

d = ImageDraw.Draw(img)
icon = Image.open(os.path.join(ROOT, "src", "app.ico"))
icon.size = max(icon.ico.sizes())
icon = icon.convert("RGBA").resize((96, 96), Image.LANCZOS)
img.alpha_composite(icon, (64, 70))

d.text((64, 190), "HDRDisplay", font=font("seguibl.ttf", 74), fill=(255, 255, 255))
d.text((64, 268), "Calibrate", font=font("seguibl.ttf", 74), fill=(96, 205, 255))
body = font("segoeui.ttf", 29)
d.text((66, 380), "SDR + HDR calibration for Windows", font=body, fill=(215, 215, 220))
d.text((66, 422), "with an ordinary colorimeter.", font=body, fill=(215, 215, 220))
d.text((66, 474), "Native MHC2 · ΔE · gamut · 3D LUT", font=body, fill=(255, 200, 61))
d.text((66, 580), "github.com/AndriyOrlov/HDRDisplayCalibrate", font=font("segoeui.ttf", 21), fill=(150, 150, 155))

img.convert("RGB").save(os.path.join(DOCS, "banner.png"), optimize=True)
print("docs/banner.png")
