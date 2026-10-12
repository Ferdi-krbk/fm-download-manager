import os
import math
from PIL import Image, ImageDraw, ImageFont, ImageFilter

def get_font(name, size):
    paths = [
        rf"C:\Windows\Fonts\{name}.ttf",
        rf"C:\Windows\Fonts\{name}.otf",
        r"C:\Windows\Fonts\segoeui.ttf",
        r"C:\Windows\Fonts\arial.ttf"
    ]
    for p in paths:
        if os.path.exists(p):
            try:
                return ImageFont.truetype(p, size)
            except Exception:
                pass
    return ImageFont.load_default()

def create_rounded_image(im, radius):
    mask = Image.new("L", im.size, 0)
    draw = ImageDraw.Draw(mask)
    draw.rounded_rectangle([(0, 0), im.size], radius=radius, fill=255)
    
    result = Image.new("RGBA", im.size, (0, 0, 0, 0))
    result.paste(im, (0, 0), mask=mask)
    return result

def main():
    W, H = 1920, 1080
    bg = Image.new("RGBA", (W, H), (9, 10, 14, 255)) # Dark obsidian
    
    # 1. Subtle, elegant ambient radial glow (Burgundy #2a0e14)
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    glow_draw = ImageDraw.Draw(glow)
    cx, cy = W // 2, H // 2 + 60
    
    # Draw soft concentric circles for radial glow
    for r in range(600, 50, -20):
        alpha = int(24 * (1.0 - (r / 600.0)))
        glow_draw.ellipse([cx - r * 1.5, cy - r, cx + r * 1.5, cy + r], fill=(201, 42, 62, alpha))
        
    glow = glow.filter(ImageFilter.GaussianBlur(60))
    bg = Image.alpha_composite(bg, glow)

    draw = ImageDraw.Draw(bg)

    # 2. Refined, Minimal Typography (Apple/Linear style)
    font_hero = get_font("bahnschrift", 42)
    font_sub = get_font("segoeui", 18)
    font_badge = get_font("segoeui", 12)

    # Main Title
    title = "FM Download Manager"
    # Measure
    bbox_t = draw.textbbox((0, 0), title, font=font_hero)
    tw = bbox_t[2] - bbox_t[0]
    tx = (W - tw) // 2
    ty = 65
    draw.text((tx, ty), title, fill=(255, 255, 255, 255), font=font_hero)

    # Subtitle
    sub = "Modern, açık kaynaklı ve yüksek hızlı Windows indirme yöneticisi."
    bbox_s = draw.textbbox((0, 0), sub, font=font_sub)
    sw = bbox_s[2] - bbox_s[0]
    sx = (W - sw) // 2
    sy = ty + 56
    draw.text((sx, sy), sub, fill=(160, 165, 185, 255), font=font_sub)

    # Minimal Badges Row
    badges = ["Windows 10 / 11", "Çok Parçalı İndirme", "Açık Kaynak & Ücretsiz", "v3.2.0"]
    badge_y = sy + 38
    total_badge_w = 0
    badge_widths = []
    for b in badges:
        bw = draw.textbbox((0, 0), b, font=font_badge)[2] + 28
        badge_widths.append(bw)
        total_badge_w += bw + 10
    total_badge_w -= 10

    bx = (W - total_badge_w) // 2
    for b, bw in zip(badges, badge_widths):
        draw.rounded_rectangle([bx, badge_y, bx + bw, badge_y + 26], radius=13,
                               fill=(20, 22, 30, 220), outline=(45, 48, 62, 255), width=1)
        tb_w = draw.textbbox((0, 0), b, font=font_badge)[2]
        draw.text((bx + (bw - tb_w) // 2, badge_y + 4), b, fill=(200, 205, 220, 255), font=font_badge)
        bx += bw + 10

    # 3. Real App Screenshot with Realistic Multi-layer Shadow
    sc_path = r"docs\ui\dashboard_dark.png"
    sc_img = Image.open(sc_path).convert("RGBA")
    
    # Scale screenshot to fit nicely: 1260 width (from 1140)
    target_w = 1260
    target_h = int(sc_img.height * (target_w / sc_img.width))
    sc_img = sc_img.resize((target_w, target_h), Image.Resampling.LANCZOS)
    
    # Rounded corners on screenshot
    rounded_sc = create_rounded_image(sc_img, radius=12)

    # Window position
    win_x = (W - target_w) // 2
    win_y = badge_y + 48

    # Multi-layer Shadow
    # Deep ambient blur
    shadow_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    s_draw = ImageDraw.Draw(shadow_layer)
    s_draw.rounded_rectangle([win_x - 10, win_y + 20, win_x + target_w + 10, win_y + target_h + 35],
                             radius=24, fill=(0, 0, 0, 160))
    shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(35))
    bg = Image.alpha_composite(bg, shadow_layer)

    # Crisp contact shadow
    contact_layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    c_draw = ImageDraw.Draw(contact_layer)
    c_draw.rounded_rectangle([win_x, win_y + 8, win_x + target_w, win_y + target_h + 12],
                             radius=14, fill=(0, 0, 0, 110))
    contact_layer = contact_layer.filter(ImageFilter.GaussianBlur(12))
    bg = Image.alpha_composite(bg, contact_layer)

    # Paste screenshot
    bg.paste(rounded_sc, (win_x, win_y), mask=rounded_sc)

    # 1px Subtle Glass Border around window
    b_draw = ImageDraw.Draw(bg)
    b_draw.rounded_rectangle([win_x, win_y, win_x + target_w, win_y + target_h],
                             radius=12, fill=None, outline=(65, 70, 90, 180), width=1)

    # Final Output
    final_rgb = bg.convert("RGB")
    out_png = r"dist\fm_clean_poster.png"
    out_jpg = r"dist\fm_clean_poster.jpg"
    final_rgb.save(out_png, quality=95)
    final_rgb.save(out_jpg, quality=95)

    print("Clean, elegant poster created successfully!")
    print("PNG:", out_png)
    print("JPG:", out_jpg)

if __name__ == "__main__":
    main()
