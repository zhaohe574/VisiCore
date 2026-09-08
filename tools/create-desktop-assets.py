"""生成 VisiCore（视枢）客户端图标和安装向导位图，与 Web SVG 共用造型和配色。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
assets = root / "src/VideoPlatform.Desktop/Assets"
assets.mkdir(parents=True, exist_ok=True)
font_path = "C:/Windows/Fonts/msyh.ttc"

# 与 web-admin/public/visicore.svg 的视口一致，超采样保证小图标边缘平滑。
scale = 16
logo = Image.new("RGBA", (64 * scale, 64 * scale))
mark = ImageDraw.Draw(logo)
def box(values):
    return tuple(round(value * scale) for value in values)
mark.rounded_rectangle(box((0, 0, 64, 64)), radius=16 * scale, fill="#12314b")
eye = []
for a, b, c, d in (
    ((10, 32), (10, 32), (18, 18), (32, 18)),
    ((32, 18), (46, 18), (54, 32), (54, 32)),
    ((54, 32), (54, 32), (46, 46), (32, 46)),
    ((32, 46), (18, 46), (10, 32), (10, 32)),
):
    for step in range(41):
        t = step / 40
        eye.append(tuple(round(scale * ((1-t)**3*a[n] + 3*(1-t)**2*t*b[n] + 3*(1-t)*t*t*c[n] + t**3*d[n])) for n in (0, 1)))
mark.line(eye, fill="#8edbe8", width=3 * scale, joint="curve")
mark.line([box(point) for point in ((22, 27), (32, 40), (42, 27))], fill="white", width=4 * scale, joint="curve")
for x, y in ((22, 27), (32, 40), (42, 27)):
    mark.ellipse(box((x-2, y-2, x+2, y+2)), fill="white")
mark.ellipse(box((29, 21, 35, 27)), fill="#8edbe8")
logo = logo.resize((256, 256), Image.Resampling.LANCZOS)
logo.save(assets / "Brand.png")
logo.save(assets / "AppIcon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

banner = Image.new("RGB", (493, 58), "#FAFAFA")
banner.paste(logo.resize((36, 36)), (440, 11), logo.resize((36, 36)))
ImageDraw.Draw(banner).line((0, 57, 493, 57), fill="#8edbe8", width=2)
banner.save(assets / "InstallerBanner.bmp")
dialog = Image.new("RGB", (493, 312), "#FAFAFA")
draw = ImageDraw.Draw(dialog)
draw.rectangle((0, 0, 163, 312), fill="#12314b")
dialog.paste(logo.resize((68, 68)), (46, 52), logo.resize((68, 68)))
draw.text((27, 142), "VisiCore", font=ImageFont.truetype(font_path, 24), fill="white")
draw.text((58, 178), "视枢", font=ImageFont.truetype(font_path, 20), fill="white")
draw.text((40, 215), "桌面客户端", font=ImageFont.truetype(font_path, 14), fill="#B7BBC2")
draw.rectangle((22, 269, 141, 272), fill="#8edbe8")
dialog.save(assets / "InstallerDialog.bmp")
print("客户端图标和中文安装向导位图已生成。")
