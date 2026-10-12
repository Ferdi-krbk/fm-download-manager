import os
import math
import subprocess
from PIL import Image, ImageDraw, ImageFont

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
    W, H = 960, 540
    num_frames = 36
    out_dir = r"dist\gif_frames"
    os.makedirs(out_dir, exist_ok=True)

    font_title = get_font("bahnschrift", 14)
    font_bold_lg = get_font("bahnschrift", 22)
    font_bold_md = get_font("bahnschrift", 16)
    font_reg = get_font("segoeui", 12)
    font_sm = get_font("segoeui", 10)
    font_mono = get_font("consola", 11)

    # Color Palette (Dark Burgundy + AMOLED)
    BG_DARK = (14, 16, 21)
    SURFACE = (22, 24, 32)
    SURFACE_CARD = (28, 30, 40)
    LINE_BORDER = (45, 48, 62)
    ACCENT_RED = (201, 42, 62)
    ACCENT_RED_HOVER = (229, 62, 62)
    TEXT_MAIN = (245, 246, 250)
    TEXT_DIM = (145, 150, 168)
    TEXT_MUTED = (95, 100, 118)
    GREEN_SUCCESS = (34, 197, 94)

    # Pre-generate smooth wave points for the speed graph
    speed_wave = []
    for i in range(num_frames + 60):
        # Base around 102 MB/s with realistic network breathing
        val = 101.5 + 4.5 * math.sin(i * 0.25) + 2.0 * math.cos(i * 0.45)
        speed_wave.append(val)

    for frame_idx in range(num_frames):
        img = Image.new("RGB", (W, H), BG_DARK)
        draw = ImageDraw.Draw(img)

        # 1. TOP HEADER (Custom Title Bar)
        draw_rounded_rect(draw, (0, 0, W, 44), 0, fill=SURFACE, outline=LINE_BORDER, width=1)
        
        # Logo Icon Monogram
        draw_rounded_rect(draw, (14, 8, 42, 36), 6, fill=ACCENT_RED)
        draw.text((20, 11), "FM", fill=(255, 255, 255), font=font_title)

        # App Title & Version Badge
        draw.text((52, 13), "FM DOWNLOAD MANAGER", fill=TEXT_MAIN, font=font_bold_md)
        draw_rounded_rect(draw, (238, 12, 318, 32), 4, fill=(45, 20, 26), outline=ACCENT_RED, width=1)
        draw.text((244, 14), "v3.2.0 TURBO", fill=ACCENT_RED_HOVER, font=font_sm)

        # Header Badges (Right side)
        draw_rounded_rect(draw, (W - 370, 10, W - 210, 34), 4, fill=(20, 36, 24), outline=GREEN_SUCCESS, width=1)
        draw.text((W - 362, 14), "✓ Eklenti Eşleşti (Chrome/Edge)", fill=GREEN_SUCCESS, font=font_sm)

        draw_rounded_rect(draw, (W - 195, 10, W - 90, 34), 4, fill=ACCENT_RED)
        draw.text((W - 182, 13), "+ URL Ekle", fill=(255, 255, 255), font=font_reg)

        # Window controls mockup
        draw.text((W - 65, 10), "─  □  ✕", fill=TEXT_DIM, font=font_reg)

        # 2. LEFT SIDEBAR (Width: 230)
        sidebar_x = 0
        sidebar_w = 230
        draw_rounded_rect(draw, (sidebar_x, 44, sidebar_x + sidebar_w, H), 0, fill=SURFACE, outline=LINE_BORDER, width=1)

        # Speed Card
        card_bbox = (12, 56, sidebar_w - 12, 230)
        draw_rounded_rect(draw, card_bbox, 8, fill=SURFACE_CARD, outline=LINE_BORDER, width=1)

        current_speed = speed_wave[frame_idx + 15]
        draw.text((24, 68), "TOPLAM HIZ", fill=TEXT_DIM, font=font_sm)
        
        # Pulsing Red Indicator
        pulse = 0.5 + 0.5 * math.sin(frame_idx * 0.4)
        dot_col = (int(201 + 54 * pulse), 42, 62)
        draw.ellipse((95, 71, 101, 77), fill=dot_col)

        draw.text((24, 84), f"{current_speed:.1f} MB/s", fill=TEXT_MAIN, font=font_bold_lg)
        draw.text((24, 114), "1 Gbps Fiber · 16 Segment", fill=GREEN_SUCCESS, font=font_sm)

        # Speed Graph (inside card)
        gx, gy, gw, gh = 24, 132, 158, 60
        draw_rounded_rect(draw, (gx, gy, gx + gw, gy + gh), 4, fill=(18, 19, 26), outline=LINE_BORDER, width=1)
        draw.text((gx + gw - 64, gy + 4), "Pik: 112,5 MB/s", fill=TEXT_MUTED, font=font_sm)

        # Draw smooth wave inside graph
        pts = []
        samples = 30
        for s in range(samples):
            px = gx + (s / (samples - 1)) * gw
            sample_val = speed_wave[frame_idx + s]
            # normalize 70-115 to gy+gh -> gy
            norm = (sample_val - 70) / 45.0
            py = (gy + gh - 4) - norm * (gh - 12)
            pts.append((px, py))

        # Fill under curve
        fill_poly = [(gx, gy + gh)] + pts + [(gx + gw, gy + gh)]
        draw.polygon(fill_poly, fill=(50, 18, 25))

        # Glowing line
        for k in range(len(pts) - 1):
            draw.line([pts[k], pts[k + 1]], fill=ACCENT_RED_HOVER, width=2)

        # Speed Limiter row
        draw.text((24, 198), "Hız Limiti:", fill=TEXT_DIM, font=font_sm)
        draw.text((82, 198), "Sınırsız (Turbo)", fill=ACCENT_RED_HOVER, font=font_sm)

        # Sidebar Categories List
        cats = [
            ("⚡ Tüm İndirmeler", "3", True),
            ("🎬 Videolar & Müzik", "1", False),
            ("📦 Programlar", "1", False),
            ("🗜️ Arşivler", "1", False),
            ("🧲 Torrentler", "0", False)
        ]
        cy = 246
        for name, count, active in cats:
            if active:
                draw_rounded_rect(draw, (12, cy - 2, sidebar_w - 12, cy + 24), 5, fill=(45, 20, 26), outline=ACCENT_RED, width=1)
                draw.text((24, cy + 2), name, fill=TEXT_MAIN, font=font_reg)
                draw.text((sidebar_w - 32, cy + 2), count, fill=ACCENT_RED_HOVER, font=font_reg)
            else:
                draw.text((24, cy + 2), name, fill=TEXT_DIM, font=font_reg)
                draw.text((sidebar_w - 32, cy + 2), count, fill=TEXT_MUTED, font=font_reg)
            cy += 32

        # 3. MAIN TABLE AREA (X: 242, Y: 44)
        tx = 242
        tw = W - tx - 12

        # Table Header
        thy = 54
        draw.text((tx + 14, thy), "DOSYA ADI", fill=TEXT_MUTED, font=font_sm)
        draw.text((tx + 280, thy), "BOYUT", fill=TEXT_MUTED, font=font_sm)
        draw.text((tx + 360, thy), "İLERLEME", fill=TEXT_MUTED, font=font_sm)
        draw.text((tx + 470, thy), "HIZ", fill=TEXT_MUTED, font=font_sm)
        draw.text((tx + 540, thy), "SEGMENT", fill=TEXT_MUTED, font=font_sm)
        draw.text((tx + 600, thy), "DURUM", fill=TEXT_MUTED, font=font_sm)

        draw.line([(tx, thy + 20), (tx + tw, thy + 20)], fill=LINE_BORDER, width=1)

        # Items in Table
        # Item 1: High speed multi-segment
        prog1 = 0.65 + 0.15 * (frame_idx / num_frames)
        row1_y = thy + 30
        draw_rounded_rect(draw, (tx, row1_y, tx + tw, row1_y + 60), 6, fill=SURFACE_CARD, outline=(70, 30, 38), width=1)

        draw.text((tx + 14, row1_y + 12), "Cyberpunk_2077_Phantom_Liberty.zip", fill=TEXT_MAIN, font=font_bold_md)
        draw.text((tx + 14, row1_y + 34), "fitgirl-repacks.site · Yüksek Hızlı HTTP/2", fill=TEXT_MUTED, font=font_sm)

        draw.text((tx + 280, row1_y + 20), "32,4 GB", fill=TEXT_DIM, font=font_reg)

        # Progress bar 1
        pb_x, pb_y, pb_w, pb_h = tx + 360, row1_y + 22, 90, 8
        draw_rounded_rect(draw, (pb_x, pb_y, pb_x + pb_w, pb_y + pb_h), 4, fill=(40, 42, 54))
        draw_rounded_rect(draw, (pb_x, pb_y, pb_x + int(pb_w * prog1), pb_y + pb_h), 4, fill=ACCENT_RED)
        draw.text((pb_x + pb_w + 6, row1_y + 18), f"{int(prog1 * 100)}%", fill=TEXT_MAIN, font=font_mono)

        draw.text((tx + 470, row1_y + 20), f"{current_speed:.1f} MB/s", fill=ACCENT_RED_HOVER, font=font_bold_md)
        draw.text((tx + 548, row1_y + 20), "16", fill=GREEN_SUCCESS, font=font_bold_md)
        draw.text((tx + 600, row1_y + 20), "İndiriliyor ⚡", fill=ACCENT_RED_HOVER, font=font_reg)

        # Item 2: YouTube 4K stream
        prog2 = 0.40 + 0.12 * (frame_idx / num_frames)
        row2_y = row1_y + 70
        draw_rounded_rect(draw, (tx, row2_y, tx + tw, row2_y + 60), 6, fill=SURFACE, outline=LINE_BORDER, width=1)

        draw.text((tx + 14, row2_y + 12), "YouTube 4K 60FPS HDR Showcase [GI8qFUFV5U0].mp4", fill=TEXT_MAIN, font=font_bold_md)
        draw.text((tx + 14, row2_y + 34), "YouTube · --http-chunk-size 10M (Hız Kalkanı Aktif)", fill=GREEN_SUCCESS, font=font_sm)

        draw.text((tx + 280, row2_y + 20), "4,8 GB", fill=TEXT_DIM, font=font_reg)

        # Progress bar 2
        draw_rounded_rect(draw, (pb_x, row2_y + 22, pb_x + pb_w, row2_y + pb_h + 22), 4, fill=(40, 42, 54))
        draw_rounded_rect(draw, (pb_x, row2_y + 22, pb_x + int(pb_w * prog2), row2_y + pb_h + 22), 4, fill=(220, 140, 30))
        draw.text((pb_x + pb_w + 6, row2_y + 18), f"{int(prog2 * 100)}%", fill=TEXT_MAIN, font=font_mono)

        yt_speed = 78.5 + 3.0 * math.sin(frame_idx * 0.3)
        draw.text((tx + 470, row2_y + 20), f"{yt_speed:.1f} MB/s", fill=TEXT_MAIN, font=font_bold_md)
        draw.text((tx + 548, row2_y + 20), "8", fill=TEXT_DIM, font=font_bold_md)
        draw.text((tx + 600, row2_y + 20), "İndiriliyor", fill=(220, 140, 30), font=font_reg)

        # Item 3: Completed Linux ISO
        row3_y = row2_y + 70
        draw_rounded_rect(draw, (tx, row3_y, tx + tw, row3_y + 50), 6, fill=SURFACE, outline=LINE_BORDER, width=1)
        draw.text((tx + 14, row3_y + 16), "ubuntu-24.04.1-desktop-amd64.iso", fill=TEXT_DIM, font=font_reg)
        draw.text((tx + 280, row3_y + 16), "5,9 GB", fill=TEXT_MUTED, font=font_reg)
        draw.text((tx + 360, row3_y + 16), "100%", fill=GREEN_SUCCESS, font=font_mono)
        draw.text((tx + 470, row3_y + 16), "0 B/s", fill=TEXT_MUTED, font=font_reg)
        draw.text((tx + 548, row3_y + 16), "-", fill=TEXT_MUTED, font=font_reg)
        draw.text((tx + 600, row3_y + 16), "Tamamlandı ✓", fill=GREEN_SUCCESS, font=font_reg)

        # Bottom Bar: Stats summary
        draw_rounded_rect(draw, (tx, H - 42, tx + tw, H - 10), 6, fill=SURFACE_CARD, outline=LINE_BORDER, width=1)
        draw.text((tx + 14, H - 32), "🚀 FM Download Manager Turbo Engine | .NET 8 WPF | Açık Kaynak & Ücretsiz", fill=TEXT_DIM, font=font_sm)
        draw.text((tx + tw - 170, H - 32), "github.com/Ferdi-krbk", fill=ACCENT_RED_HOVER, font=font_sm)

        frame_path = os.path.join(out_dir, f"frame_{frame_idx:03d}.png")
        img.save(frame_path)

    print("Frames rendered successfully. Combining into GIF with ffmpeg...")

    ffmpeg_bin = r"C:\Users\Ferdi\AppData\Local\Microsoft\WinGet\Packages\Gyan.FFmpeg_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-9.0.2-full_build\bin\ffmpeg.exe"
    output_gif = r"dist\fm_download_manager_demo.gif"
    docs_gif = r"docs\ui\demo_turbo.gif"
    os.makedirs(r"docs\ui", exist_ok=True)

    # Use palettegen and paletteuse for optimal quality and small size
    palette_cmd = [
        ffmpeg_bin, "-y",
        "-framerate", "18",
        "-i", os.path.join(out_dir, "frame_%03d.png"),
        "-vf", "palettegen=stats_mode=diff",
        os.path.join(out_dir, "palette.png")
    ]
    subprocess.run(palette_cmd, check=True)

    gif_cmd = [
        ffmpeg_bin, "-y",
        "-framerate", "18",
        "-i", os.path.join(out_dir, "frame_%03d.png"),
        "-i", os.path.join(out_dir, "palette.png"),
        "-lavfi", "paletteuse=dither=bayer:bayer_scale=3",
        output_gif
    ]
    subprocess.run(gif_cmd, check=True)

    # Also copy to docs/ui
    import shutil
    shutil.copy2(output_gif, docs_gif)
    print("GIF generated at:", output_gif)
    print("GIF copied to:", docs_gif)

if __name__ == "__main__":
    main()
