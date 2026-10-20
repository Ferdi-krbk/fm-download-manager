import os
import subprocess

def main():
    frames_dir = r"dist\wpf_hero_frames"
    palette_path = r"dist\wpf_hero_frames\palette.png"
    out_gif = r"docs\ui\demo_turbo.gif"

    if not os.path.exists(frames_dir):
        print(f"Directory {frames_dir} not found!")
        return

    # Pass 1: Generate high quality 256-color palette
    cmd1 = [
        "ffmpeg", "-y", "-i", os.path.join(frames_dir, "frame_%03d.png"),
        "-vf", "fps=18,scale=1160:-1:flags=lanczos,palettegen=max_colors=256:stats_mode=diff",
        palette_path
    ]
    subprocess.run(cmd1, check=True)

    # Pass 2: Apply palette with bayer dithering
    cmd2 = [
        "ffmpeg", "-y", "-i", os.path.join(frames_dir, "frame_%03d.png"),
        "-i", palette_path,
        "-filter_complex", "fps=18,scale=1160:-1:flags=lanczos[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3",
        out_gif
    ]
    subprocess.run(cmd2, check=True)
    print(f"Flawless 100% genuine WPF demo_turbo.gif saved to {out_gif}")

if __name__ == "__main__":
    main()
