# Copyright (c) 2026 New Vision contributors
# SPDX-License-Identifier: AGPL-3.0-only

"""Draws the New Vision app icon: dark background, a crisp green detection frame and 'NV'."""
import sys
from PIL import Image, ImageDraw, ImageFont

SIZE = 1024
BG = (16, 20, 24, 255)
GREEN = (92, 253, 155, 255)  # same color as the detection boxes

img = Image.new("RGBA", (SIZE, SIZE), BG)
d = ImageDraw.Draw(img)

# Detection frame with square corners
m, w = 170, 34
DY = 45  # tab sticks out above the frame, shift down to center the whole picture
d.rectangle([m, m + DY, SIZE - m, SIZE - m + DY], outline=GREEN, width=w)

# Label tab on the top-left of the frame, like a detection label
d.rectangle([m, m - 90 + DY, m + 330, m + DY], fill=GREEN)
tab_font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 62)
d.text((m + 22, m - 80 + DY), "Vision", font=tab_font, fill=BG)

# Big NV in the middle
font = ImageFont.truetype("C:/Windows/Fonts/arialbd.ttf", 330)
text = "NV"
box = d.textbbox((0, 0), text, font=font)
x = (SIZE - (box[2] - box[0])) / 2 - box[0]
y = (SIZE - (box[3] - box[1])) / 2 - box[1] + 30 + DY
d.text((x, y), text, font=font, fill=GREEN)

img.save(sys.argv[1])
print("saved", sys.argv[1])
