from io import BytesIO
from pathlib import Path
from shutil import copyfile
from subprocess import check_output

from PIL import Image, ImageDraw, ImageFont


PROJECT = Path(__file__).resolve().parents[1]
OUT = PROJECT / "out"
REFERENCE = PROJECT.parent / "promo" / "out" / "mochidock-short.mp4"
DRAFT = OUT / "mochidock-hyperframes-draft.mp4"

FRAMES = [
    (0.50, "Idle capsule"),
    (2.60, "Hover opens Media"),
    (4.20, "CPU fully loaded"),
    (9.20, "Power history"),
    (11.50, "Gaming overlay"),
    (13.80, "FX rates"),
    (16.30, "Weather"),
    (18.80, "Prayer countdown"),
    (21.20, "Synced lyrics"),
    (24.10, "Themes"),
    (27.05, "Mascots"),
    (30.30, "End card"),
]


def font(size, weight):
    return ImageFont.truetype(str(PROJECT / "assets" / f"Quicksand-{weight}.ttf"), size)


def frame(path, time):
    png = check_output([
        "ffmpeg", "-v", "error", "-ss", str(time), "-i", str(path),
        "-vf", "scale=171:304:flags=lanczos", "-frames:v", "1", "-c:v", "png",
        "-f", "image2pipe", "pipe:1",
    ])
    return Image.open(BytesIO(png)).convert("RGB")


def main():
    sheet = Image.new("RGB", (1296, 1700), "#171420")
    draw = ImageDraw.Draw(sheet)
    title, beat, small = font(48, "Bold"), font(26, "Bold"), font(17, "SemiBold")
    draw.text((44, 25), "MochiDock promo comparison", fill="#f6f1ff", font=title)
    draw.text((46, 91), "Same timecode in both edits  •  HyperFrames draft on the left  •  Approved Remotion on the right",
              fill="#c6bedb", font=small)

    for index, (time, label) in enumerate(FRAMES):
        x, y = 44 + (index % 3) * 408, 134 + (index // 3) * 386
        draw.rounded_rectangle((x, y, x + 390, y + 366), 13, fill="#272235")
        draw.text((x + 16, y + 9), label, fill="#f6f1ff", font=beat)
        draw.text((x + 373, y + 15), f"00:{time:05.2f}", anchor="ra", fill="#d7f69c", font=small)
        sheet.paste(frame(DRAFT, time), (x + 16, y + 49))
        sheet.paste(frame(REFERENCE, time), (x + 202, y + 49))

    sheet.save(OUT / "mochidock-hyperframes-storyboard.png", optimize=True)
    copyfile(PROJECT / "review" / "compare.html", OUT / "compare.html")


if __name__ == "__main__":
    main()
