"""使用项目根目录 Logo 生成客户端图标和安装向导位图。"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

root = Path(__file__).resolve().parents[1]
assets = root / "src/VideoPlatform.Desktop/Assets"
assets.mkdir(parents=True, exist_ok=True)
font_path = "C:/Windows/Fonts/msyh.ttc"

logo = Image.open(root / "图片.png").convert("RGBA")
logo.thumbnail((256, 256), Image.Resampling.LANCZOS)
canvas = Image.new("RGBA", (256, 256), (255, 255, 255, 0))
canvas.alpha_composite(logo, ((256 - logo.width) // 2, (256 - logo.height) // 2))
logo = canvas
logo.save(assets / "Brand.png")
logo.save(assets / "AppIcon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])

banner = Image.new("RGB", (493, 58), "#FAFAFA")
banner.paste(logo.resize((36, 36)), (440, 11), logo.resize((36, 36)))
ImageDraw.Draw(banner).line((0, 57, 493, 57), fill="#D83D47", width=2)
banner.save(assets / "InstallerBanner.bmp")
dialog = Image.new("RGB", (493, 312), "#FAFAFA")
draw = ImageDraw.Draw(dialog)
draw.rectangle((0, 0, 163, 312), fill="#282B30")
dialog.paste(logo.resize((68, 68)), (46, 52), logo.resize((68, 68)))
draw.text((22, 148), "京华安防平台", font=ImageFont.truetype(font_path, 18), fill="white")
draw.text((40, 180), "桌面客户端", font=ImageFont.truetype(font_path, 14), fill="#B7BBC2")
draw.rectangle((22, 269, 141, 272), fill="#D83D47")
dialog.save(assets / "InstallerDialog.bmp")
print("客户端图标和中文安装向导位图已生成。")
