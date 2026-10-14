import os
import math
import subprocess
import shutil
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

def draw_rounded_rect(draw, bbox, radius, fill=None, outline=None, width=1):
    draw.rounded_rectangle(bbox, radius=radius, fill=fill, outline=outline, width=width)

def main():
    W, H = 1280, 720
    num_frames = 36
    fps = 18
    out_dir = r"dist\clean_gif_frames"
    os.makedirs(out_dir, exist_ok=True)

    font_title = get_font("bahnschrift", 13)
    font_bold_lg = get_font("bahnschrift", 22)
    font_bold_md = get_font("bahnschrift", 14)
    font_reg = get_font("segoeui", 12)
    font_sm = get_font("segoeui", 10)
    font_mono = get_font("consola", 11)

    # Color Palette - Sleek Modern Dark Burgundy (#C92A3E)
    BG_CANVAS = (9, 10, 14)
    WIN_BG = (15, 17, 23)
    SURFACE = (22, 24, 32)
    SURFACE_CARD = (28, 30, 40)
    LINE_BORDER = (45, 48, 62)
    ACCENT_RED = (201, 42, 62)
    ACCENT_RED_HOVER = (229, 62, 62)
    TEXT_MAIN = (245, 246, 250)
    TEXT_DIM = (150, 155, 172)
    TEXT_MUTED = (95, 100, 118)
    GREEN_SUCCESS = (34, 197, 94)

    # Window Dimensions centered on canvas
    win_w, win_h = 1040, 580
    win_x = (W - win_w) // 2
    win_y = (H - win_h) // 2

    # Pre-calculate waveform
    wave_samples = []
    for i in range(num_frames + 60):
        val = 102.4 + 3.8 * math.sin(i * 0.22) + 1.6 * math.cos(i * 0.44)
        wave_samples.append(val)

    for f_idx in range(num_frames):
        # Base Canvas
        canvas = Image.new("RGBA", (W, H), BG_CANVAS + (255,))

        # 1. Subtle, Breathing Ambient Burgundy Glow
        breath = 0.5 + 0.5 * math.sin(f_idx * (2 * math.pi / num_frames))
        glow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        g_draw = ImageDraw.Draw(glow)
        cx, cy = W // 2, H // 2
        for r in range(500, 80, -30):
            alpha = int((14 + 8 * breath) * (1.0 - (r / 500.0)))
            g_draw.ellipse([cx - r * 1.4, cy - r, cx + r * 1.4, cy + r], fill=(201, 42, 62, alpha))
        glow = glow.filter(ImageFilter.GaussianBlur(50))
        canvas = Image.alpha_composite(canvas, glow)

        # 2. Window Drop Shadow
        shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        s_draw = ImageDraw.Draw(shadow)
        s_draw.rounded_rectangle([win_x - 8, win_y + 14, win_x + win_w + 8, win_y + win_h + 26],
                                 radius=20, fill=(0, 0, 0, 150))
        shadow = shadow.filter(ImageFilter.GaussianBlur(24))
        canvas = Image.alpha_composite(canvas, shadow)

        # Draw Window Frame
        draw = ImageDraw.Draw(canvas)
        draw_rounded_rect(draw, (win_x, win_y, win_x + win_w, win_y + win_h), radius=12, fill=WIN_BG, outline=(55, 60, 78), width=1)

        # Window Title Bar (Height: 40)
        tb_h = 40
        draw_rounded_rect(draw, (win_x, win_y, win_x + win_w, win_y + tb_h), radius=12, fill=SURFACE)
        # Cover bottom radius of title bar
        draw.rectangle([win_x, win_y + tb_h - 12, win_x + win_w, win_y + tb_h], fill=SURFACE)
        draw.line([(win_x, win_y + tb_h), (win_x + win_w, win_y + tb_h)], fill=LINE_BORDER, width=1)

        # Monogram Icon & Title
        draw_rounded_rect(draw, (win_x + 14, win_y + 8, win_x + 38, win_y + 32), radius=5, fill=ACCENT_RED)
        draw.text((win_x + 19, win_y + 11), "FM", fill=(255, 255, 255), font=font_title)
        draw.text((win_x + 48, win_y + 12), "FM Download Manager", fill=TEXT_MAIN, font=font_bold_md)

        # Clean Version Badge
        draw_rounded_rect(draw, (win_x + 208, win_y + 11, win_x + 265, win_y + 29), radius=4,
                          fill=(35, 18, 24), outline=ACCENT_RED, width=1)
        draw.text((win_x + 216, win_y + 13), "v3.4.3", fill=ACCENT_RED_HOVER, font=font_sm)

        # Window Controls (mac/win subtle dots or icons)
        draw.text((win_x + win_w - 75, win_y + 12), "─   □   ✕", fill=TEXT_DIM, font=font_sm)

        # 3. Left Sidebar (Width: 230)
        sb_w = 230
        draw.rectangle([win_x, win_y + tb_h, win_x + sb_w, win_y + win_h - 12], fill=SURFACE)
        draw.line([(win_x + sb_w, win_y + tb_h), (win_x + sb_w, win_y + win_h)], fill=LINE_BORDER, width=1)

        # Speed Widget Card
        card_x = win_x + 14
        card_y = win_y + tb_h + 16
        card_w = sb_w - 28
        card_h = 168
        draw_rounded_rect(draw, (card_x, card_y, card_x + card_w, card_y + card_h), radius=8,
                          fill=SURFACE_CARD, outline=LINE_BORDER, width=1)

        cur_speed = wave_samples[f_idx + 18]
        draw.text((card_x + 14, card_y + 12), "TOPLAM HIZ", fill=TEXT_DIM, font=font_sm)
        
        # Breathing dot
        dot_alpha = int(200 + 55 * math.sin(f_idx * 0.35))
        draw.ellipse([card_x + card_w - 24, card_y + 14, card_x + card_w - 16, card_y + 22],
                     fill=(dot_alpha, 42, 62))

        draw.text((card_x + 14, card_y + 28), f"{cur_speed:.1f} MB/s", fill=TEXT_MAIN, font=font_bold_lg)
        draw.text((card_x + 14, card_y + 56), "1 aktif indirme · 16 segment", fill=GREEN_SUCCESS, font=font_sm)

        # Waveform inside card
        gx = card_x + 14
        gy = card_y + 76
        gw = card_w - 28
        gh = 56
        draw_rounded_rect(draw, (gx, gy, gx + gw, gy + gh), radius=4, fill=(18, 19, 25), outline=LINE_BORDER, width=1)

        # Grid lines
        draw.line([(gx, gy + gh // 2), (gx + gw, gy + gh // 2)], fill=(32, 34, 44), width=1)

        # Smooth Bezier Wave
        pts = []
        samples_count = 28
        for s in range(samples_count):
            px = gx + (s / (samples_count - 1)) * gw
            val = wave_samples[f_idx + s]
            norm = (val - 85.0) / 30.0
            py = (gy + gh - 4) - norm * (gh - 10)
            pts.append((px, py))

        fill_poly = [(gx, gy + gh)] + pts + [(gx + gw, gy + gh)]
        draw.polygon(fill_poly, fill=(45, 18, 25))
        for k in range(len(pts) - 1):
            draw.line([pts[k], pts[k + 1]], fill=ACCENT_RED_HOVER, width=2)

        draw.text((gx + gw - 65, gy + 3), "Pik: 110,4 MB/s", fill=TEXT_MUTED, font=font_sm)

        # Limiter row
        draw.text((card_x + 14, card_y + 142), "Hız Limiti:", fill=TEXT_DIM, font=font_sm)
        draw.text((card_x + 75, card_y + 142), "Sınırsız", fill=ACCENT_RED_HOVER, font=font_sm)

        # Categories
        cat_y = card_y + card_h + 20
        cats = [
            ("Tüm İndirmeler", "2", True),
            ("Videolar", "1", False),
            ("Programlar", "1", False),
            ("Arşivler", "0", False),
            ("Torrentler", "0", False)
        ]
        for c_name, c_cnt, active in cats:
            if active:
                draw_rounded_rect(draw, (card_x, cat_y, card_x + card_w, cat_y + 26), radius=5, fill=(45, 20, 26), outline=ACCENT_RED, width=1)
                draw.text((card_x + 12, cat_y + 4), c_name, fill=TEXT_MAIN, font=font_reg)
                draw.text((card_x + card_w - 20, cat_y + 4), c_cnt, fill=ACCENT_RED_HOVER, font=font_reg)
            else:
                draw.text((card_x + 12, cat_y + 4), c_name, fill=TEXT_DIM, font=font_reg)
                draw.text((card_x + card_w - 20, cat_y + 4), c_cnt, fill=TEXT_MUTED, font=font_reg)
            cat_y += 34

        # 4. Main Table
        content_x = win_x + sb_w + 18
        content_w = win_w - sb_w - 36

        # Action Toolbar Row
        at_y = win_y + tb_h + 14
        draw_rounded_rect(draw, (content_x, at_y, content_x + 105, at_y + 30), radius=5, fill=ACCENT_RED)
        draw.text((content_x + 14, at_y + 6), "+ İndirme Ekle", fill=(255, 255, 255), font=font_reg)

        draw_rounded_rect(draw, (content_x + 115, at_y, content_x + 235, at_y + 30), radius=5, fill=(24, 26, 35), outline=LINE_BORDER, width=1)
        draw.text((content_x + 128, at_y + 6), "Tarayıcı Eklentisi", fill=TEXT_DIM, font=font_reg)

        # Table Column Headers
        th_y = at_y + 44
        draw.text((content_x + 12, th_y), "DOSYA ADI", fill=TEXT_MUTED, font=font_sm)
        draw.text((content_x + 320, th_y), "BOYUT", fill=TEXT_MUTED, font=font_sm)
        draw.text((content_x + 400, th_y), "İLERLEME", fill=TEXT_MUTED, font=font_sm)
        draw.text((content_x + 530, th_y), "HIZ", fill=TEXT_MUTED, font=font_sm)
        draw.text((content_x + 620, th_y), "SEG.", fill=TEXT_MUTED, font=font_sm)
        draw.text((content_x + 675, th_y), "DURUM", fill=TEXT_MUTED, font=font_sm)

        draw.line([(content_x, th_y + 20), (content_x + content_w, th_y + 20)], fill=LINE_BORDER, width=1)

        # Row 1: High speed download
        r1_y = th_y + 30
        draw_rounded_rect(draw, (content_x, r1_y, content_x + content_w, r1_y + 64), radius=6, fill=SURFACE_CARD, outline=(65, 30, 38), width=1)

        draw.text((content_x + 14, r1_y + 12), "Cyberpunk_2077_Update_2.13.zip", fill=TEXT_MAIN, font=font_bold_md)
        draw.text((content_x + 14, r1_y + 36), "fitgirl-repacks.site · Çoklu Akış", fill=TEXT_MUTED, font=font_sm)

        draw.text((content_x + 320, r1_y + 22), "14,8 GB", fill=TEXT_DIM, font=font_reg)

        prog1 = 0.58 + 0.14 * (f_idx / num_frames)
        pb_w = 95
        draw_rounded_rect(draw, (content_x + 400, r1_y + 26, content_x + 400 + pb_w, r1_y + 34), radius=4, fill=(35, 38, 50))
        draw_rounded_rect(draw, (content_x + 400, r1_y + 26, content_x + 400 + int(pb_w * prog1), r1_y + 34), radius=4, fill=ACCENT_RED)
        draw.text((content_x + 400 + pb_w + 8, r1_y + 22), f"{int(prog1 * 100)}%", fill=TEXT_MAIN, font=font_mono)

        draw.text((content_x + 530, r1_y + 22), f"{cur_speed:.1f} MB/s", fill=ACCENT_RED_HOVER, font=font_bold_md)
        draw.text((content_x + 625, r1_y + 22), "16", fill=GREEN_SUCCESS, font=font_bold_md)
        draw.text((content_x + 675, r1_y + 22), "İndiriliyor", fill=ACCENT_RED_HOVER, font=font_reg)

        # Row 2: Media with Dual-Audio & Subtitles
        r2_y = r1_y + 74
        draw_rounded_rect(draw, (content_x, r2_y, content_x + content_w, r2_y + 64), radius=6, fill=SURFACE, outline=LINE_BORDER, width=1)

        draw.text((content_x + 14, r2_y + 12), "The_Matrix_Resurrections_1080p.mp4", fill=TEXT_MAIN, font=font_bold_md)
        draw.text((content_x + 14, r2_y + 36), "Çift Ses (TR Dublaj + Orijinal) · Türkçe Altyazı (.srt)", fill=(229, 62, 62), font=font_sm)

        draw.text((content_x + 320, r2_y + 22), "2,4 GB", fill=TEXT_DIM, font=font_reg)

        prog2 = 0.35 + 0.10 * (f_idx / num_frames)
        draw_rounded_rect(draw, (content_x + 400, r2_y + 26, content_x + 400 + pb_w, r2_y + 34), radius=4, fill=(35, 38, 50))
        draw_rounded_rect(draw, (content_x + 400, r2_y + 26, content_x + 400 + int(pb_w * prog2), r2_y + 34), radius=4, fill=(220, 140, 30))
        draw.text((content_x + 400 + pb_w + 8, r2_y + 22), f"{int(prog2 * 100)}%", fill=TEXT_MAIN, font=font_mono)

        yt_speed = 76.4 + 2.5 * math.sin(f_idx * 0.3)
        draw.text((content_x + 530, r2_y + 22), f"{yt_speed:.1f} MB/s", fill=TEXT_MAIN, font=font_bold_md)
        draw.text((content_x + 625, r2_y + 22), "8", fill=TEXT_DIM, font=font_bold_md)
        draw.text((content_x + 675, r2_y + 22), "İndiriliyor", fill=(220, 140, 30), font=font_reg)

        # Row 3: Completed ISO
        r3_y = r2_y + 74
        draw_rounded_rect(draw, (content_x, r3_y, content_x + content_w, r3_y + 50), radius=6, fill=SURFACE, outline=LINE_BORDER, width=1)
        draw.text((content_x + 14, r3_y + 16), "ubuntu-24.04.1-desktop-amd64.iso", fill=TEXT_DIM, font=font_reg)
        draw.text((content_x + 320, r3_y + 16), "5,9 GB", fill=TEXT_MUTED, font=font_reg)
        draw.text((content_x + 400, r3_y + 16), "100%", fill=GREEN_SUCCESS, font=font_mono)
        draw.text((content_x + 530, r3_y + 16), "0 B/s", fill=TEXT_MUTED, font=font_reg)
        draw.text((content_x + 625, r3_y + 16), "-", fill=TEXT_MUTED, font=font_reg)
        draw.text((content_x + 675, r3_y + 16), "Tamamlandı ✓", fill=GREEN_SUCCESS, font=font_reg)

        # Save frame
        frame_file = os.path.join(out_dir, f"frame_{f_idx:03d}.png")
        canvas.convert("RGB").save(frame_file)

    print("Frames rendered. Creating clean GIF with ffmpeg...")
    ffmpeg_bin = r"C:\Users\Ferdi\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe"
    out_gif = r"dist\fm_clean_animation.gif"

    palette_cmd = [
        ffmpeg_bin, "-y",
        "-framerate", str(fps),
        "-i", os.path.join(out_dir, "frame_%03d.png"),
        "-vf", "palettegen=stats_mode=diff",
        os.path.join(out_dir, "palette.png")
    ]
    subprocess.run(palette_cmd, check=True)

    gif_cmd = [
        ffmpeg_bin, "-y",
        "-framerate", str(fps),
        "-i", os.path.join(out_dir, "frame_%03d.png"),
        "-i", os.path.join(out_dir, "palette.png"),
        "-lavfi", "paletteuse=dither=bayer:bayer_scale=3",
        out_gif
    ]
    subprocess.run(gif_cmd, check=True)

    # Sync to target locations
    shutil.copy2(out_gif, r"dist\fm_download_manager_demo.gif")
    shutil.copy2(out_gif, r"docs\ui\demo_turbo.gif")
    shutil.copy2(out_gif, r"C:\Users\Ferdi\.gemini\antigravity\brain\fe48d779-583f-4b7a-8a69-e02a89214517\fm_download_manager_demo.gif")

    print("Ultra-clean GIF created successfully at:", out_gif)

if __name__ == "__main__":
    main()
