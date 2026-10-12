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

def create_base_canvas(w, h, glow_cx, glow_cy, glow_r=500):
    bg = Image.new("RGBA", (w, h), (9, 10, 14, 255))
    glow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    g_draw = ImageDraw.Draw(glow)
    for r in range(glow_r, 40, -25):
        alpha = int(22 * (1.0 - (r / glow_r)))
        g_draw.ellipse([glow_cx - r * 1.5, glow_cy - r, glow_cx + r * 1.5, glow_cy + r], fill=(201, 42, 62, alpha))
    glow = glow.filter(ImageFilter.GaussianBlur(55))
    return Image.alpha_composite(bg, glow)

def draw_vector_lightning(draw, x, y, size=24, color=(229, 62, 62)):
    # Clean lightning bolt polygon
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

def draw_vector_disk(draw, x, y, size=24, color=(229, 62, 62)):
    # Sleek disk/database layer icon
    s = size / 24.0
    for dy in [4 * s, 10 * s, 16 * s]:
        draw.rounded_rectangle([x + 3 * s, y + dy, x + 21 * s, y + dy + 5 * s], radius=2, fill=color)

def draw_vector_graph(draw, x, y, size=24, color=(229, 62, 62)):
    # Sleek graph bars
    s = size / 24.0
    draw.rectangle([x + 4 * s, y + 14 * s, x + 7 * s, y + 20 * s], fill=color)
    draw.rectangle([x + 10 * s, y + 8 * s, x + 13 * s, y + 20 * s], fill=color)
    draw.rectangle([x + 16 * s, y + 3 * s, x + 19 * s, y + 20 * s], fill=color)

def draw_vector_play(draw, x, y, size=24, color=(255, 255, 255)):
    # Clean play triangle
    s = size / 24.0
    pts = [
        (x + 7 * s, y + 4 * s),
        (x + 19 * s, y + 12 * s),
        (x + 7 * s, y + 20 * s)
    ]
    draw.polygon(pts, fill=color)

def draw_vector_globe(draw, x, y, size=24, color=(255, 255, 255)):
    # Clean globe
    s = size / 24.0
    draw.ellipse([x + 3 * s, y + 3 * s, x + 21 * s, y + 21 * s], outline=color, width=int(max(1, 2 * s)))
    draw.line([(x + 3 * s, y + 12 * s), (x + 21 * s, y + 12 * s)], fill=color, width=int(max(1, 1.5 * s)))
    draw.ellipse([x + 7 * s, y + 3 * s, x + 17 * s, y + 21 * s], outline=color, width=int(max(1, 1.5 * s)))

def draw_header(draw, w, title, subtitle, badge=None):
    font_badge = get_font("segoeui", 12)
    font_title = get_font("bahnschrift", 42)
    font_sub = get_font("segoeui", 18)

    y = 60
    if badge:
        bbox_b = draw.textbbox((0, 0), badge, font=font_badge)
        bw = bbox_b[2] - bbox_b[0] + 28
        bx = (w - bw) // 2
        draw.rounded_rectangle([bx, y, bx + bw, y + 26], radius=13, fill=(35, 18, 24, 220), outline=(201, 42, 62, 255), width=1)
        draw.text((bx + 14, y + 4), badge, fill=(229, 62, 62, 255), font=font_badge)
        y += 42

    bbox_t = draw.textbbox((0, 0), title, font=font_title)
    tw = bbox_t[2] - bbox_t[0]
    draw.text(((w - tw) // 2, y), title, fill=(255, 255, 255, 255), font=font_title)
    y += 54

    bbox_s = draw.textbbox((0, 0), subtitle, font=font_sub)
    sw = bbox_s[2] - bbox_s[0]
    draw.text(((w - sw) // 2, y), subtitle, fill=(160, 165, 185, 255), font=font_sub)
    return y + 48

# ─────────────────────────────────────────────────────────────
# POSTER 1: Hero Launch Poster
# ─────────────────────────────────────────────────────────────
def generate_poster_1():
    W, H = 1920, 1080
    canvas = create_base_canvas(W, H, W // 2, H // 2 + 80, glow_r=600)
    draw = ImageDraw.Draw(canvas)

    bottom_header_y = draw_header(
        draw, W,
        title="FM Download Manager",
        subtitle="Hızın ve sadeliğin yeni standardı. Windows için modern, açık kaynaklı indirme yöneticisi.",
        badge="V3.2.0 TURBO EDITION"
    )

    # Real Screenshot
    sc = Image.open(r"docs\ui\dashboard_dark.png").convert("RGBA")
    tw = 1260
    th = int(sc.height * (tw / sc.width))
    sc = sc.resize((tw, th), Image.Resampling.LANCZOS)

    mask = Image.new("L", (tw, th), 0)
    ImageDraw.Draw(mask).rounded_rectangle([(0, 0), (tw, th)], radius=14, fill=255)
    rounded_sc = Image.new("RGBA", (tw, th), (0, 0, 0, 0))
    rounded_sc.paste(sc, (0, 0), mask=mask)

    win_x = (W - tw) // 2
    win_y = bottom_header_y + 12

    # Shadow
    shadow = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([win_x - 10, win_y + 20, win_x + tw + 10, win_y + th + 35], radius=24, fill=(0, 0, 0, 160))
    shadow = shadow.filter(ImageFilter.GaussianBlur(36))
    canvas = Image.alpha_composite(canvas, shadow)

    canvas.paste(rounded_sc, (win_x, win_y), mask=rounded_sc)
    ImageDraw.Draw(canvas).rounded_rectangle([win_x, win_y, win_x + tw, win_y + th], radius=14, outline=(65, 70, 90, 180), width=1)

    out_path = r"dist\afis_1_hero.jpg"
    canvas.convert("RGB").save(out_path, quality=95)
    print("Poster 1 created successfully without emoji bugs:", out_path)

# ─────────────────────────────────────────────────────────────
# POSTER 2: Performance Showcase
# ─────────────────────────────────────────────────────────────
def generate_poster_2():
    W, H = 1920, 1080
    canvas = create_base_canvas(W, H, W // 2 + 250, H // 2, glow_r=550)
    draw = ImageDraw.Draw(canvas)

    draw_header(
        draw, W,
        title="1 Gbps Fiber. Sıfır Tıkanma.",
        subtitle="Tekil sunucu kotalarını kırarak bağlantınızı son damlasına kadar doldurun.",
        badge="PERFORMANS & MIMARI"
    )

    font_card_title = get_font("bahnschrift", 22)
    font_card_desc = get_font("segoeui", 14)
    font_metric_lg = get_font("bahnschrift", 48)
    font_metric_sm = get_font("segoeui", 13)

    cards = [
        ("lightning", "16-32 Paralel Bağlantı",
         "Tekil HTTP akışlarında sunucuların uyguladığı 5–7 MB/s hız kotasını aşmak için dosyayı 16 ve 32 bağımsız segmente böler."),
        ("disk", "256 KB Yüksek Hızlı Disk Tamponu",
         "Saniyede binlerce gereksiz sistem çağrısını önler. 100 MB/s hızda SSD ve HDD darboğazlarını sıfırlayarak %1 CPU tüketir."),
        ("graph", "Pürüzsüz Canlı Hız Grafiği (EMA)",
         "Parça değişimlerinde yaşanan saliselik sahte düşüşleri üstel hareketli ortalama ile filtreler, kaya gibi stabil bir hız eğrisi sunar.")
    ]

    card_x = 180
    card_y = 260
    card_w = 680
    card_h = 190

    for icon_type, title, desc in cards:
        draw.rounded_rectangle([card_x, card_y, card_x + card_w, card_y + card_h], radius=12,
                               fill=(22, 24, 32, 230), outline=(45, 48, 62, 255), width=1)
        
        # Draw vector icon container
        draw.rounded_rectangle([card_x + 24, card_y + 20, card_x + 60, card_y + 56], radius=8, fill=(35, 20, 26), outline=(201, 42, 62), width=1)
        if icon_type == "lightning":
            draw_vector_lightning(draw, card_x + 30, card_y + 26, size=24, color=(229, 62, 62))
        elif icon_type == "disk":
            draw_vector_disk(draw, card_x + 30, card_y + 26, size=24, color=(229, 62, 62))
        elif icon_type == "graph":
            draw_vector_graph(draw, card_x + 30, card_y + 26, size=24, color=(229, 62, 62))

        draw.text((card_x + 72, card_y + 26), title, fill=(255, 255, 255), font=font_card_title)
        
        words = desc.split(" ")
        line = ""
        line_y = card_y + 72
        for w in words:
            if len(line + " " + w) > 65:
                draw.text((card_x + 28, line_y), line, fill=(160, 165, 185), font=font_card_desc)
                line = w
                line_y += 24
            else:
                line = line + " " + w if line else w
        if line:
            draw.text((card_x + 28, line_y), line, fill=(160, 165, 185), font=font_card_desc)

        card_y += card_h + 24

    # Right Column: Big Glass Telemetry Card
    rc_x = 900
    rc_y = 260
    rc_w = 840
    rc_h = 620

    draw.rounded_rectangle([rc_x, rc_y, rc_x + rc_w, rc_y + rc_h], radius=16,
                           fill=(18, 20, 28, 240), outline=(65, 30, 38, 255), width=1)

    draw.text((rc_x + 40, rc_y + 40), "CANLI AĞ AKIŞI", fill=(145, 150, 168), font=font_metric_sm)
    draw.ellipse([rc_x + rc_w - 60, rc_y + 42, rc_x + rc_w - 48, rc_y + 54], fill=(229, 62, 62))

    draw.text((rc_x + 40, rc_y + 64), "104,2 MB/s", fill=(255, 255, 255), font=font_metric_lg)
    draw.text((rc_x + 40, rc_y + 130), "1 Gbps Fiber Doygunluğu  |  16 Segment Aktif", fill=(34, 197, 94), font=font_card_desc)

    gx = rc_x + 40
    gy = rc_y + 175
    gw = rc_w - 80
    gh = 230
    draw.rounded_rectangle([gx, gy, gx + gw, gy + gh], radius=8, fill=(12, 13, 18), outline=(45, 48, 62), width=1)

    for ly in [0.25, 0.5, 0.75]:
        draw.line([(gx, gy + int(gh * ly)), (gx + gw, gy + int(gh * ly))], fill=(25, 27, 36), width=1)

    pts = []
    num_pts = 45
    for i in range(num_pts):
        px = gx + (i / (num_pts - 1)) * gw
        val = 101.5 + 4.5 * math.sin(i * 0.28) + 2.0 * math.cos(i * 0.55)
        norm = (val - 75.0) / 40.0
        py = (gy + gh - 8) - norm * (gh - 24)
        pts.append((px, py))

    fill_poly = [(gx, gy + gh)] + pts + [(gx + gw, gy + gh)]
    draw.polygon(fill_poly, fill=(50, 18, 25))
    for k in range(len(pts) - 1):
        draw.line([pts[k], pts[k + 1]], fill=(229, 62, 62), width=3)

    draw.text((gx + gw - 120, gy + 12), "Pik: 112,5 MB/s", fill=(120, 125, 140), font=font_metric_sm)

    stat_y = rc_y + 440
    stats = [
        ("Bağlantı Sayısı", "16 Parça"),
        ("I/O Tamponu", "256 KB"),
        ("Soket Havuzu", "64 Eşzamanlı"),
        ("Hız Limiti", "Sınırsız (Turbo)")
    ]
    sw = (gw - 45) // 4
    for idx, (label, val_s) in enumerate(stats):
        bx = gx + idx * (sw + 15)
        draw.rounded_rectangle([bx, stat_y, bx + sw, stat_y + 130], radius=10, fill=(24, 26, 36), outline=(45, 48, 62), width=1)
        draw.text((bx + 18, stat_y + 24), label, fill=(145, 150, 168), font=font_metric_sm)
        draw.text((bx + 18, stat_y + 54), val_s, fill=(255, 255, 255), font=get_font("bahnschrift", 18))

    out_path = r"dist\afis_2_performance.jpg"
    canvas.convert("RGB").save(out_path, quality=95)
    print("Poster 2 created successfully without emoji bugs:", out_path)

# ─────────────────────────────────────────────────────────────
# POSTER 3: Features & Integration
# ─────────────────────────────────────────────────────────────
def generate_poster_3():
    W, H = 1920, 1080
    canvas = create_base_canvas(W, H, W // 2, H // 2, glow_r=550)
    draw = ImageDraw.Draw(canvas)

    draw_header(
        draw, W,
        title="Akıllı Medya Yakalayıcısı & Tarayıcı Entegrasyonu",
        subtitle="YouTube'un hız sınırını aşın, Chromium tarayıcılarıyla sıfır tıkla doğrudan eşleşin.",
        badge="ONE CIKAN YETENEKLER"
    )

    card_y = 260
    card_w = 720
    card_h = 660

    # Left Card: YouTube Shield
    c1_x = 200
    draw.rounded_rectangle([c1_x, card_y, c1_x + card_w, card_y + card_h], radius=16,
                           fill=(20, 22, 30, 240), outline=(50, 55, 72, 255), width=1)

    # Clean Vector Play Icon Box
    draw.rounded_rectangle([c1_x + 36, card_y + 36, c1_x + 96, card_y + 96], radius=12, fill=(201, 42, 62))
    draw_vector_play(draw, c1_x + 50, card_y + 50, size=36, color=(255, 255, 255))

    draw.text((c1_x + 116, card_y + 44), "YouTube Hız Kalkanı & Format Seçici", fill=(255, 255, 255), font=get_font("bahnschrift", 24))
    draw.text((c1_x + 116, card_y + 76), "yt-dlp & FFmpeg Entegre Video Motoru", fill=(145, 150, 168), font=get_font("segoeui", 14))

    yt_features = [
        ("10 MB HTTP Range Kalkanı", "YouTube'un bağlantıyı 10–15 MB/s'ye kısmasını engellemek için her 10 MB'da bir taze istek açar, videoyu 40–100 MB/s pik hızda indirir."),
        ("4K, 1080p ve 720p HD Seçimi", "Tek tıkla istediğiniz çözünürlüğü seçebileceğiniz modern kalite seçim penceresi ekrana fırlar."),
        ("Sadece Ses Çıkarıcı (MP3)", "Müzik kliplerini ve podcast'leri anında 320 kbps MP3 formatına dönüştürerek arşivler."),
        ("Twitter/X & TikTok Yakalama", "Tweet detaylarındaki videoları ve TikTok içeriklerini CDN engellerine takılmadan otomatik algılar.")
    ]

    fy = card_y + 130
    for f_title, f_desc in yt_features:
        draw.rounded_rectangle([c1_x + 36, fy, c1_x + card_w - 36, fy + 105], radius=10, fill=(26, 28, 38), outline=(42, 45, 58), width=1)
        
        # Indicator tag
        draw.rounded_rectangle([c1_x + 54, fy + 18, c1_x + 60, fy + 34], radius=3, fill=(201, 42, 62))
        draw.text((c1_x + 70, fy + 16), f_title, fill=(255, 255, 255), font=get_font("bahnschrift", 16))
        
        words = f_desc.split(" ")
        line = ""
        line_y = fy + 44
        for w in words:
            if len(line + " " + w) > 58:
                draw.text((c1_x + 54, line_y), line, fill=(150, 155, 172), font=get_font("segoeui", 12))
                line = w
                line_y += 20
            else:
                line = line + " " + w if line else w
        if line:
            draw.text((c1_x + 54, line_y), line, fill=(150, 155, 172), font=get_font("segoeui", 12))
        fy += 122

    # Right Card: Zero-Click Browser Pairing
    c2_x = 1000
    draw.rounded_rectangle([c2_x, card_y, c2_x + card_w, card_y + card_h], radius=16,
                           fill=(20, 22, 30, 240), outline=(50, 55, 72, 255), width=1)

    # Clean Vector Globe Icon Box
    draw.rounded_rectangle([c2_x + 36, card_y + 36, c2_x + 96, card_y + 96], radius=12, fill=(34, 197, 94))
    draw_vector_globe(draw, c2_x + 50, card_y + 50, size=36, color=(255, 255, 255))

    draw.text((c2_x + 116, card_y + 44), "Sıfır-Tık Tarayıcı Eklentisi (Zero-Click)", fill=(255, 255, 255), font=get_font("bahnschrift", 24))
    draw.text((c2_x + 116, card_y + 76), "Chromium Manifest V3 Güvenli Köprü", fill=(145, 150, 168), font=get_font("segoeui", 14))

    ext_features = [
        ("Sıfır-Tık Token Tohumlama", "Eklenti klasörüne otomatik güvenlik anahtarı yerleştirilir; kod kopyalayıp yapıştırmakla uğraşmadan tek tıkla bağlanır."),
        ("Chrome, Edge ve Brave Desteği", "Tek tıkla tarayıcınızın uzantı yöneticisini açar ve geliştirici modunda saniyeler içinde kurulur."),
        ("Otomatik İndirme Onayı", "Tarayıcıdan bir dosya indirildiğinde doğrudan sessizce başlamaz; ekrana dosya adı ve klasör seçimi içeren onay kutusu açar."),
        ("100% Yerel & Gizlilik Odaklı", "Tüm köprü iletişimi 127.0.0.1 yerel arayüzünde kalır; hiçbir kullanıcı verisi dışarı sızmaz veya toplanmaz.")
    ]

    fy2 = card_y + 130
    for f_title, f_desc in ext_features:
        draw.rounded_rectangle([c2_x + 36, fy2, c2_x + card_w - 36, fy2 + 105], radius=10, fill=(26, 28, 38), outline=(42, 45, 58), width=1)
        
        # Indicator tag
        draw.rounded_rectangle([c2_x + 54, fy2 + 18, c2_x + 60, fy2 + 34], radius=3, fill=(34, 197, 94))
        draw.text((c2_x + 70, fy2 + 16), f_title, fill=(255, 255, 255), font=get_font("bahnschrift", 16))

        words = f_desc.split(" ")
        line = ""
        line_y = fy2 + 44
        for w in words:
            if len(line + " " + w) > 58:
                draw.text((c2_x + 54, line_y), line, fill=(150, 155, 172), font=get_font("segoeui", 12))
                line = w
                line_y += 20
            else:
                line = line + " " + w if line else w
        if line:
            draw.text((c2_x + 54, line_y), line, fill=(150, 155, 172), font=get_font("segoeui", 12))
        fy2 += 122

    out_path = r"dist\afis_3_features.jpg"
    canvas.convert("RGB").save(out_path, quality=95)
    print("Poster 3 created successfully without emoji bugs:", out_path)

if __name__ == "__main__":
    generate_poster_1()
    generate_poster_2()
    generate_poster_3()
