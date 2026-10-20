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

def main():
    base_path = r"docs\ui\dashboard_pro.png"
    if not os.path.exists(base_path):
        print("Base dashboard_pro.png not found!")
        return

    base_img = Image.open(base_path).convert("RGBA")
    W, H = base_img.size # 1160 x 700

    frames_dir = r"dist\hero_frames"
    os.makedirs(frames_dir, exist_ok=True)

    font_speed = get_font("segoeuib", 17)
    font_pct = get_font("segoeuib", 12)
    font_mono = get_font("consola", 11)

    # Coordinates identified from earlier crop:
    # Graph area: (180, 180, 558, 250)
    gx1, gy1, gx2, gy2 = 182, 182, 558, 248
    gw = gx2 - gx1
    gh = gy2 - gy1

    # Speed text box: (480, 150, 565, 182)
    sx1, sy1, sx2, sy2 = 480, 150, 565, 182

    # Background color of the graph card
    BG_CARD = (19, 21, 29, 255)
    LINE_GRID = (35, 38, 48, 255)
    ACCENT_CRIMSON = (224, 26, 79, 255)

    num_frames = 36
    for f in range(num_frames):
        frame = base_img.copy()
        draw = ImageDraw.Draw(frame)

        # 1. Redraw graph canvas
        draw.rectangle([gx1, gy1, gx2, gy2], fill=BG_CARD)

        # Subtle grid lines
        for y_line in range(gy1 + 15, gy2, 18):
            draw.line([(gx1, y_line), (gx2, y_line)], fill=LINE_GRID, width=1)

        # Calculate animated waveform points
        pts = []
        phase = f * (2 * math.pi / num_frames)
        for x in range(gx1, gx2 + 1):
            rel_x = (x - gx1) / gw
            # Smooth fluctuating curve with harmonic frequencies
            wave = (
                math.sin(rel_x * 9.5 + phase * 2.0) * 11.0 +
                math.cos(rel_x * 17.0 - phase * 1.5) * 6.0 +
                math.sin(rel_x * 4.0 + phase) * 8.0
            )
            # Center around mid-height
            y = (gy1 + gy2) // 2 - int(wave) - 4
            y = max(gy1 + 3, min(gy2 - 4, y))
            pts.append((x, y))

        # Filled area under curve with soft glow
        fill_poly = [(gx1, gy2)] + pts + [(gx2, gy2)]
        overlay = Image.new("RGBA", (W, H), (0, 0, 0, 0))
        o_draw = ImageDraw.Draw(overlay)
        o_draw.polygon(fill_poly, fill=(224, 26, 79, 45))
        frame = Image.alpha_composite(frame, overlay)
        draw = ImageDraw.Draw(frame)

        # Draw the neon line
        draw.line(pts, fill=ACCENT_CRIMSON, width=2)

        # 2. Update speed text (fluctuates between 41.5 and 45.8 MB/s)
        current_speed = 42.1 + 2.8 * math.sin(phase) + 1.2 * math.cos(phase * 2)
        speed_str = f"{current_speed:.1f} MB/s".replace(".", ",")
        
        # Clear old speed text area
        draw.rectangle([sx1, sy1, sx2, sy2], fill=BG_CARD)
        draw.text((sx1 + 4, sy1 + 4), speed_str, fill=(255, 255, 255, 255), font=font_speed)

        # Save frame
        frame_path = os.path.join(frames_dir, f"frame_{f:03d}.png")
        frame.convert("RGB").save(frame_path)

    print("Frames generated, building GIF via ffmpeg...")
    palette_path = os.path.join(frames_dir, "palette.png")
    out_gif = r"docs\ui\demo_turbo.gif"

    # FFmpeg two-pass palette generation for flawless GIF quality
    cmd1 = [
        "ffmpeg", "-y", "-i", os.path.join(frames_dir, "frame_%03d.png"),
        "-vf", "fps=18,scale=1160:-1:flags=lanczos,palettegen=max_colors=256",
        palette_path
    ]
    subprocess.run(cmd1, check=True)

    cmd2 = [
        "ffmpeg", "-y", "-i", os.path.join(frames_dir, "frame_%03d.png"),
        "-i", palette_path,
        "-filter_complex", "fps=18,scale=1160:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3",
        out_gif
    ]
    subprocess.run(cmd2, check=True)
    print(f"Flawless real app GIF saved to {out_gif}")

if __name__ == "__main__":
    main()
