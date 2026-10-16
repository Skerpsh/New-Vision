"""Checks the Gemini API key and the request New Vision sends, from the PC.

Usage: python Tools/test_gemini.py [path/to/gemini_api_key.txt] [image.jpg|png]
Options via environment variables: MODEL (default gemini-3.8-flash), SEARCH=1 to add Google Search grounding,
THINK=low|medium|high ("" = model default).
The key is read from the file and never printed.
"""
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request

model = os.environ.get("MODEL", "gemini-3.8-flash")
search = os.environ.get("SEARCH", "0") == "1"
think = os.environ.get("THINK", "low")
key_path = sys.argv[1] if len(sys.argv) > 1 else r"C:\New_Vision\secrets\gemini_api_key.txt"
image_path = sys.argv[2] if len(sys.argv) > 2 else "Assets/NewVision/AppIcon.png"

key = open(key_path, encoding="utf-8").read().strip()
image = open(image_path, "rb").read()
mime = "image/png" if image_path.lower().endswith(".png") else "image/jpeg"
body = {
    "systemInstruction": {"parts": [{"text": "Answer in English, plain text, at most 40 words."}]},
    "contents": [{"role": "user", "parts": [
        {"inline_data": {"mime_type": mime, "data": base64.b64encode(image).decode()}},
        {"text": "What is in this image?"}]}],
}
if search:
    body["tools"] = [{"google_search": {}}]
if think:
    body["generationConfig"] = {"thinkingConfig": {"thinkingLevel": think}}

request = urllib.request.Request(
    f"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse",
    data=json.dumps(body).encode(),
    headers={"Content-Type": "application/json", "x-goog-api-key": key})
start = time.time()
first = None
try:
    with urllib.request.urlopen(request, timeout=60) as response:
        text = ""
        for line in response:
            line = line.decode().strip()
            if line.startswith("data:"):
                chunk = json.loads(line[5:])
                for part in chunk.get("candidates", [{}])[0].get("content", {}).get("parts", []):
                    if not part.get("thought") and part.get("text"):
                        first = first or time.time() - start
                        text += part["text"]
        print(f"OK {model} search={search} think={think or 'default'}: first text {first or 0:.1f} s, "
              f"total {time.time() - start:.1f} s\n{text.strip()}")
except urllib.error.HTTPError as e:
    print(f"HTTP {e.code} {model} search={search}:", e.read().decode()[:600])
