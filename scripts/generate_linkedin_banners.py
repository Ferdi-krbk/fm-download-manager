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

def draw_vector_lightning(draw, x, y, size=24, color=(201, 42, 62)):
    s = size / 24.0
    pts = [
        (x + 13 * s, y + 2 * s),
        (x + 5 * s, y + 13 * s),
        (x + 11 * s, y + 13 * s),
        (x + 9 * s, y + 22 * s),
        (x + 19 * s, y + 9 * s),
        (x + 13 * s, y + 9 * s)
    ]
    draw.polygon(pts, fill=color)

def generate_profile_cover(out_path):
    # LinkedIn Profile Cover: 1584 x 396
    W, H = 1584, 396
    bg = Image.new("RGBA", (W, H), (9, 10, 14, 255))
    
    # Ambient Burgundy Glow
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    cx, cy = W - 350, H // 2
    for r in range(400, 30, -20):
        alpha = int(28 * (1.0 - (r / 400.0)))
        g_draw.ellipse([cx - r * 1.6, cy - r, cx + r * 1.6, cy + r], fill=(201, 42, 62, alpha))
    glow = glow.filter(ImageFilter.GaussianBlur(45))
    bg = Image.alpha_composite(bg, glow)
    
    draw = ImageDraw.Draw(bg)
    
    # Decorative subtle grid/lines
    for y in range(40, H, 60):
        draw.line([(0, y), (W, y)], fill=(25, 28, 38, 70), width=1)
    
    # Left Content
    # Logo badge
    draw.rounded_rectangle([90, 80, 160, 150], radius=16, fill=(201, 42, 62, 255))
    font_logo = get_font("bahnschrift", 34)
    draw.text((104, 92), "FM", fill=(255, 255, 255), font=font_logo)
    
    # Title
    font_title = get_font("bahnschrift", 44)
    draw.text((180, 82), "FM Download Manager", fill=(255, 255, 255), font=font_title)
    
    # Subtitle
    font_sub = get_font("segoeui", 20)
    draw.text((182, 140), "Windows İçin Açık Kaynaklı & 1 Gbps Gigabit İndirme Yöneticisi", fill=(170, 175, 195), font=font_sub)
    
    # Tech Tags Row
    font_tag = get_font("segoeui", 14)
    tags = [
        ("⚡ 1 Gbps Gigabit Motor", (201, 42, 62)),
        ("💻 C# • .NET 8 • WPF", (59, 130, 246)),
        ("🎬 Evrensel Video & HLS", (168, 85, 247)),
        ("🧲 BitTorrent Entegre", (16, 185, 129)),
        ("🌐 Chrome & Edge Eklentisi", (245, 158, 11))
    ]
    
    tag_x = 90
    tag_y = 230
    for text, color in tags:
        bbox = draw.textbbox((0, 0), text, font=font_tag)
        tw = bbox[2] - bbox[0]
        # pill bg
        draw.rounded_rectangle([tag_x, tag_y, tag_x + tw + 24, tag_y + 36], radius=18, fill=(18, 20, 28, 240), outline=(45, 50, 70, 200), width=1)
        draw.text((tag_x + 12, tag_y + 7), text, fill=(230, 235, 245), font=font_tag)
        tag_x += tw + 36
        
    # GitHub URL footer line
    font_link = get_font("segoeui", 14)
    draw.text((92, 315), "github.com/Ferdi-krbk/fm-download-manager  •  Mesut Çevik MC Download Manager Windows Portu", fill=(120, 125, 145), font=font_link)
    
    # Paste mini app preview on right side if frame exists
    frame_path = r"dist\clean_gif_frames\frame_000.png"
    if os.path.exists(frame_path):
        app_img = Image.open(frame_path).convert("RGBA")
        # scale down
        target_h = 290
        ratio = target_h / float(app_img.height)
        target_w = int(app_img.width * ratio)
        app_resized = app_img.resize((target_w, target_h), Image.Resampling.LANCZOS)
        
        # Round corners
        mask = Image.new("L", app_resized.size, 0)
        m_draw = ImageDraw.Draw(mask)
        m_draw.rounded_rectangle([(0, 0), app_resized.size], radius=14, fill=255)
        
        # Shadow
        shadow = Image.new("RGBA", (target_w + 40, target_h + 40), (0, 0, 0, 0))
        s_draw = ImageDraw.Draw(shadow)
        s_draw.rounded_rectangle([(20, 20), (target_w + 20, target_h + 20)], radius=16, fill=(0, 0, 0, 180))
        shadow = shadow.filter(ImageFilter.GaussianBlur(14))
        
        px = W - target_w - 70
        py = (H - target_h) // 2 + 10
        bg.paste(shadow, (px - 20, py - 20), mask=shadow)
        bg.paste(app_resized, (px, py), mask=mask)
        
    bg.convert("RGB").save(out_path, "JPEG", quality=95)
    print(f"Saved: {out_path}")

def generate_post_banner(out_path):
    # LinkedIn Post Landscape Banner: 1200 x 628
    W, H = 1200, 628
    bg = Image.new("RGBA", (W, H), (9, 10, 14, 255))
    
    # Ambient Burgundy Glow centered bottom
    glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    cx, cy = W // 2, H - 100
    for r in range(450, 40, -25):
        alpha = int(32 * (1.0 - (r / 450.0)))
        g_draw.ellipse([cx - r * 1.5, cy - r, cx + r * 1.5, cy + r], fill=(201, 42, 62, alpha))
    glow = glow.filter(ImageFilter.GaussianBlur(50))
    bg = Image.alpha_composite(bg, glow)
    
    draw = ImageDraw.Draw(bg)
    
    # Title badge
    badge_text = "YENİ AÇIK KAYNAK LANSMANI"
    font_badge = get_font("segoeui", 12)
    bbox_b = draw.textbbox((0, 0), badge_text, font=font_badge)
    bw = bbox_b[2] - bbox_b[0]
    bx = (W - bw) // 2
    draw.rounded_rectangle([bx - 14, 35, bx + bw + 14, 65], radius=15, fill=(45, 14, 20, 220), outline=(201, 42, 62, 180), width=1)
    draw.text((bx, 42), badge_text, fill=(248, 113, 113), font=font_badge)
    
    # Main Hero Title
    font_hero = get_font("bahnschrift", 46)
    title = "FM Download Manager"
    bbox_t = draw.textbbox((0, 0), title, font=font_hero)
    tw = bbox_t[2] - bbox_t[0]
    draw.text(((W - tw) // 2, 85), title, fill=(255, 255, 255), font=font_hero)
    
    # Subtitle
    font_sub = get_font("segoeui", 18)
    sub = "Mesut Çevik'in macOS indirme yöneticisi Windows için sıfırdan port edildi!"
    bbox_s = draw.textbbox((0, 0), sub, font=font_sub)
    sw = bbox_s[2] - bbox_s[0]
    draw.text(((W - sw) // 2, 145), sub, fill=(160, 165, 185), font=font_sub)
    
    # App UI Showcase Screenshot centered
    frame_path = r"dist\clean_gif_frames\frame_000.png"
    if os.path.exists(frame_path):
        app_img = Image.open(frame_path).convert("RGBA")
        target_h = 350
        ratio = target_h / float(app_img.height)
        target_w = int(app_img.width * ratio)
        app_resized = app_img.resize((target_w, target_h), Image.Resampling.LANCZOS)
        
        # Round corners
        mask = Image.new("L", app_resized.size, 0)
        m_draw = ImageDraw.Draw(mask)
        m_draw.rounded_rectangle([(0, 0), app_resized.size], radius=12, fill=255)
        
        # Shadow
        shadow = Image.new("RGBA", (target_w + 50, target_h + 50), (0, 0, 0, 0))
        s_draw = ImageDraw.Draw(shadow)
        s_draw.rounded_rectangle([(25, 25), (target_w + 25, target_h + 25)], radius=16, fill=(0, 0, 0, 210))
        shadow = shadow.filter(ImageFilter.GaussianBlur(18))
        
        px = (W - target_w) // 2
        py = 200
        bg.paste(shadow, (px - 25, py - 25), mask=shadow)
        bg.paste(app_resized, (px, py), mask=mask)
        
    # Floating Stats Pills on sides
    font_pill_title = get_font("bahnschrift", 16)
    font_pill_sub = get_font("segoeui", 11)
    
    # Left Pill: 1 Gbps
    draw.rounded_rectangle([50, 270, 240, 345], radius=14, fill=(15, 18, 25, 230), outline=(201, 42, 62, 140), width=1)
    draw.text((70, 282), "⚡ 95.4 MB/s", fill=(255, 255, 255), font=font_pill_title)
    draw.text((70, 310), "1 Gbps Gigabit Doygunluğu", fill=(160, 165, 185), font=font_pill_sub)
    
    # Right Pill: Evrensel Medya
    draw.rounded_rectangle([W - 240, 270, W - 50, 345], radius=14, fill=(15, 18, 25, 230), outline=(59, 130, 246, 140), width=1)
    draw.text((W - 220, 282), "🎬 Evrensel Medya", fill=(255, 255, 255), font=font_pill_title)
    draw.text((W - 220, 310), "HLS • MP4 • YouTube • MP3", fill=(160, 165, 185), font=font_pill_sub)

    # Bottom Pill Left: BitTorrent
    draw.rounded_rectangle([50, 375, 240, 450], radius=14, fill=(15, 18, 25, 230), outline=(16, 185, 129, 140), width=1)
    draw.text((70, 387), "🧲 Entegre Torrent", fill=(255, 255, 255), font=font_pill_title)
    draw.text((70, 415), "Harici İstemcisiz Magnet", fill=(160, 165, 185), font=font_pill_sub)
    
    # Bottom Pill Right: Sıfır Kurulum
    draw.rounded_rectangle([W - 240, 375, W - 50, 450], radius=14, fill=(15, 18, 25, 230), outline=(168, 85, 247, 140), width=1)
    draw.text((W - 220, 387), "📦 Sıfır Bağımlılık", fill=(255, 255, 255), font=font_pill_title)
    draw.text((W - 220, 415), ".NET 8 İçe Gömülü (Self-Contained)", fill=(160, 165, 185), font=font_pill_sub)

    # Footer GitHub
    font_ft = get_font("segoeui", 13)
    ft = "github.com/Ferdi-krbk/fm-download-manager  •  %100 Ücretsiz & Açık Kaynak"
    bbox_f = draw.textbbox((0, 0), ft, font=font_ft)
    fw = bbox_f[2] - bbox_f[0]
    draw.text(((W - fw) // 2, 580), ft, fill=(140, 145, 165), font=font_ft)

    bg.convert("RGB").save(out_path, "JPEG", quality=95)
    print(f"Saved: {out_path}")

if __name__ == "__main__":
    os.makedirs("dist", exist_ok=True)
    generate_profile_cover(r"dist\linkedin_header_banner.jpg")
    generate_post_banner(r"dist\linkedin_post_banner.jpg")
