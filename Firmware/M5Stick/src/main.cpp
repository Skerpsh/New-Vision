// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only
// The drawing helpers, Impact FX and boot sequence are adapted from Vartovyi (github.com/Skerpsh/vartovyi, MIT).
//
// New Vision — M5StickC Plus2 firmware.
// The stick is mounted on the Quest 3 and works as a bridge, all over Bluetooth LE:
//   Quest (hand gestures / wrist panel) --BLE "UART" service--> stick --BLE keyboard (HID)--> laptop
// One BLE peripheral with two services and two connections at the same time (laptop + headset).
// Stick buttons are intentionally NOT used (it is fixed on the headset; touching the
// headset must never trigger anything). Everything is controlled from the Quest.
//
// The screen faces outwards: a cyberdeck HUD after Vartovyi (github.com/Skerpsh/vartovyi, MIT, by the same author):
// black glass with scanlines, one accent colour, cut corners, Tektur font, a boot sequence, and Impact FX —
// shaking the head shakes and tears the picture.
//
// Text protocol, one line per message ('\n'). Over BLE: the Quest writes lines to RX, replies come as
// notifications on TX (long lines split into chunks, the '\n' ends a line). USB serial accepts the same commands.
//   "NV?"                       -> "NVSTICK bt=0|1"            handshake (bt = laptop keyboard connected)
//   "STATUS"                    -> "STATUS bt=1 bat=87 chg=1 vol=90 bri=100 scr=home th=0 fx=2 up=123 heap=12345"
//   "CMD <name>"                -> "OK <name>" | "ERR notpaired" | "ERR unknown"   keys sent to the laptop (list below)
//   "SET vol <0..255>"          -> "OK vol"                    buzzer volume
//   "SET bri <0..255>"          -> "OK bri"                    screen brightness
//   "SET scr home|debug|off"    -> "OK scr"                    what the screen shows
//   "SET theme <0..5>"          -> "OK theme"                  accent colour (same themes as the headset)
//   "SET fx <0..2>"             -> "OK fx"                     Impact FX: off / low (knocks) / high (any shake)
//   "SET rot 0|1"               -> "OK rot"                    picture turned 180°
//   "TIME <unix> <utc offset min>" -> "OK time"                the clock on the HOME screen
//   "LOG <text>"                -> "OK log"                    an event from the headset for the log (e.g. FOUND LAPTOP)
//   "BEEP"                      -> "OK beep"
//   "REBOOT" / "OFF"            -> "OK reboot" / "OK off"
// Laptop commands (system control, sent by the headset's hand gestures):
//   play (Play/Pause), next / prev (track), volup / voldown (3 volume steps), mute     -- consumer-control keys
//   switch (Alt+Tab), tasks (Win+Tab), desktop (Win+D), snapleft / snapright (Win+Left/Right)  -- keyboard
// Settings survive a reboot.
// After changing the HID report map (nv-stick 6 added the media keys) remove "New Vision Stick" from the laptop's
// Bluetooth devices and pair again: Windows keeps the old report map of a bonded device.

#include <M5Unified.h>
#include <NimBLEDevice.h>
#include <NimBLEHIDDevice.h>
#include <Preferences.h>
#include <deque>
#include <time.h>

static const char* FW_VERSION = "nv-stick 6";
static const char* DEVICE_NAME = "New Vision Stick";
// Nordic UART Service UUIDs: the Quest finds the stick by name and talks through these
static const char* UART_SERVICE = "6E400001-B5A3-F393-E0A9-E50E24DCCA9E";
static const char* UART_RX = "6E400002-B5A3-F393-E0A9-E50E24DCCA9E";   // Quest -> stick (write)
static const char* UART_TX = "6E400003-B5A3-F393-E0A9-E50E24DCCA9E";   // stick -> Quest (notify)

// HID: keyboard (modifiers + 6 keys, report id 1) and consumer control (one 16-bit usage, report id 2: media, volume)
static const uint8_t REPORT_MAP[] = {
    0x05, 0x01, 0x09, 0x06, 0xA1, 0x01, 0x85, 0x01,
    0x05, 0x07, 0x19, 0xE0, 0x29, 0xE7, 0x15, 0x00, 0x25, 0x01, 0x75, 0x01, 0x95, 0x08, 0x81, 0x02,  // modifiers
    0x95, 0x01, 0x75, 0x08, 0x81, 0x01,                                                              // reserved
    0x95, 0x06, 0x75, 0x08, 0x15, 0x00, 0x25, 0x65, 0x05, 0x07, 0x19, 0x00, 0x29, 0x65, 0x81, 0x00,  // keys
    0xC0,
    0x05, 0x0C, 0x09, 0x01, 0xA1, 0x01, 0x85, 0x02,                                                  // consumer control
    0x15, 0x00, 0x26, 0xFF, 0x03, 0x19, 0x00, 0x2A, 0xFF, 0x03, 0x75, 0x10, 0x95, 0x01, 0x81, 0x00,  // one usage 0..0x3FF
    0xC0
};
static const uint8_t MOD_LEFT_ALT = 0x04;
static const uint8_t MOD_LEFT_GUI = 0x08;
static const uint8_t KEY_D = 0x07;
static const uint8_t KEY_TAB = 0x2B;
static const uint8_t KEY_RIGHT = 0x4F;
static const uint8_t KEY_LEFT = 0x50;
// Consumer page usages
static const uint16_t CC_NEXT = 0xB5;
static const uint16_t CC_PREV = 0xB6;
static const uint16_t CC_PLAY_PAUSE = 0xCD;
static const uint16_t CC_MUTE = 0xE2;
static const uint16_t CC_VOL_UP = 0xE9;
static const uint16_t CC_VOL_DOWN = 0xEA;

struct Command {
    const char* name;
    const char* label;
    uint8_t modifier;
    uint8_t key;        // keyboard key, or 0
    uint16_t media;     // consumer-control usage, or 0
    uint8_t repeat;     // presses in a row
};

static const Command COMMANDS[] = {
    {"play",      "PLAY/PAUSE", 0, 0, CC_PLAY_PAUSE, 1},
    {"next",      "NEXT",       0, 0, CC_NEXT, 1},
    {"prev",      "PREV",       0, 0, CC_PREV, 1},
    {"volup",     "VOL +",      0, 0, CC_VOL_UP, 3},     // Windows moves 2% per press
    {"voldown",   "VOL -",      0, 0, CC_VOL_DOWN, 3},
    {"mute",      "MUTE",       0, 0, CC_MUTE, 1},
    {"switch",    "ALT+TAB",    MOD_LEFT_ALT, KEY_TAB, 0, 1},
    {"tasks",     "TASKS",      MOD_LEFT_GUI, KEY_TAB, 0, 1},
    {"desktop",   "DESKTOP",    MOD_LEFT_GUI, KEY_D, 0, 1},
    {"snapleft",  "SNAP L",     MOD_LEFT_GUI, KEY_LEFT, 0, 1},
    {"snapright", "SNAP R",     MOD_LEFT_GUI, KEY_RIGHT, 0, 1},
};

// ======================================================================== BLE

static NimBLEServer* server;
static NimBLEHIDDevice* hid;
static NimBLECharacteristic* keyboardInput;
static NimBLECharacteristic* mediaInput;
static NimBLECharacteristic* uartTx;
static volatile uint16_t questConn = BLE_HS_CONN_HANDLE_NONE;
static volatile int connections = 0;

// Lines from the BLE task, handled in loop()
static portMUX_TYPE inboxLock = portMUX_INITIALIZER_UNLOCKED;
static std::deque<std::string> inbox;

static bool laptopConnected() { return keyboardInput && keyboardInput->getSubscribedCount() > 0; }
static bool questConnected() { return questConn != BLE_HS_CONN_HANDLE_NONE; }

class ServerCallbacks : public NimBLEServerCallbacks {
    void onConnect(NimBLEServer* s, ble_gap_conn_desc* desc) override {
        connections++;
        NimBLEDevice::startAdvertising();   // keep advertising so the other side can connect too
    }
    void onDisconnect(NimBLEServer* s, ble_gap_conn_desc* desc) override {
        connections--;
        if (desc->conn_handle == questConn) questConn = BLE_HS_CONN_HANDLE_NONE;
        NimBLEDevice::startAdvertising();
    }
};

class RxCallbacks : public NimBLECharacteristicCallbacks {
    std::string partial;
    void onWrite(NimBLECharacteristic* c, ble_gap_conn_desc* desc) override {
        questConn = desc->conn_handle;
        partial += std::string(c->getValue());
        size_t nl;
        while ((nl = partial.find('\n')) != std::string::npos) {
            std::string line = partial.substr(0, nl);
            partial.erase(0, nl + 1);
            portENTER_CRITICAL(&inboxLock);
            if (inbox.size() < 32) inbox.push_back(line);
            portEXIT_CRITICAL(&inboxLock);
        }
        if (partial.size() > 256) partial.clear();
    }
};

static void sendBle(const String& text) {
    if (!questConnected()) return;
    std::string data = std::string(text.c_str()) + "\n";
    size_t chunk = server->getPeerMTU(questConn);
    chunk = chunk > 3 ? chunk - 3 : 20;
    for (size_t i = 0; i < data.size(); i += chunk) {
        uartTx->setValue((const uint8_t*)data.data() + i, std::min(chunk, data.size() - i));
        uartTx->notify();
    }
}

// ======================================================================== settings & state

Preferences prefs;
enum ScreenMode { SCREEN_HOME, SCREEN_DEBUG, SCREEN_OFF };
static ScreenMode screenMode = SCREEN_HOME;
static uint8_t volume = 90;
static uint8_t brightness = 100;
static int themeIndex = 0;
static int fxLevel = 2;
static bool flipped = false;   // picture turned 180° (the stick mounted the other way round)

static const int LOG_LINES = 8;
static String logLines[LOG_LINES];
static int logCount = 0;
static uint32_t packets = 0;
static uint32_t lastQuestMs = 0;
static String lastEvent = "BOOT";
static String serialLine;

// Clock: the headset sends the time; the stick counts from there
static int64_t epochBase = 0;
static uint32_t epochAtMs = 0;
static int tzMinutes = 0;

static void saveSettings() {
    prefs.putUChar("vol", volume);
    prefs.putUChar("bri", brightness);
    prefs.putUChar("scr", (uint8_t)screenMode);
    prefs.putUChar("th", (uint8_t)themeIndex);
    prefs.putUChar("fx", (uint8_t)fxLevel);
    prefs.putBool("rot", flipped);
}

static void loadSettings() {
    prefs.begin("nvstick", false);
    volume = prefs.getUChar("vol", 90);
    brightness = prefs.getUChar("bri", 100);
    screenMode = (ScreenMode)constrain(prefs.getUChar("scr", SCREEN_HOME), 0, 2);
    themeIndex = constrain(prefs.getUChar("th", 0), 0, 5);
    fxLevel = constrain(prefs.getUChar("fx", 2), 0, 2);
    flipped = prefs.getBool("rot", false);
}

// ======================================================================== look (after Vartovyi)

constexpr uint16_t rgb(uint8_t r, uint8_t g, uint8_t b) { return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3); }
struct Theme {
    const char* name;
    uint8_t r, g, b;
};
static const Theme THEMES[] = {{"ORANGE", 255, 156, 0}, {"CYAN", 0, 229, 255},  {"MAGENTA", 255, 40, 190},
                               {"VIOLET", 165, 95, 255}, {"BLUE", 45, 130, 255}, {"ICE", 205, 225, 255}};
static const char* const FX_NAMES[] = {"OFF", "LOW", "HIGH"};
static const uint16_t C_BG = rgb(5, 5, 7);
static uint16_t C_ACCENT, C_DIM, C_PANEL, C_SCAN, C_TEXT, C_MUTED;
static const uint16_t C_GREEN = rgb(57, 255, 20), C_CYAN = rgb(0, 229, 255), C_YELLOW = rgb(255, 228, 0);
static const uint16_t C_RED = rgb(255, 32, 56), C_BLACK = rgb(0, 0, 0), C_WHITE = rgb(255, 255, 255);
static const uint16_t C_MAGENTA = rgb(255, 0, 200);

// Everything but the accent is the accent mixed with black, grey or white, so any theme stays one family
static uint16_t mixed(const Theme& t, int with, int pct) {
    return rgb(t.r + (with - t.r) * pct / 100, t.g + (with - t.g) * pct / 100, t.b + (with - t.b) * pct / 100);
}
static void applyTheme() {
    const Theme& t = THEMES[themeIndex];
    C_ACCENT = rgb(t.r, t.g, t.b);
    C_DIM = mixed(t, 0, 59);
    C_PANEL = mixed(t, 0, 88);
    C_MUTED = mixed(t, 120, 60);
    C_TEXT = mixed(t, 255, 85);
    C_SCAN = mixed(t, 0, 93);
}

M5Canvas canvas(&M5.Display);   // off-screen buffer (in PSRAM): redraw without flicker

extern const uint8_t VLW_T11[] asm("_binary_fonts_t11_vlw_start");
extern const uint8_t VLW_T14[] asm("_binary_fonts_t14_vlw_start");
extern const uint8_t VLW_T18[] asm("_binary_fonts_t18_vlw_start");
extern const uint8_t VLW_T24[] asm("_binary_fonts_t24_vlw_start");
extern const uint8_t VLW_T32[] asm("_binary_fonts_t32_vlw_start");
extern const uint8_t VLW_T44[] asm("_binary_fonts_t44_vlw_start");
static lgfx::VLWfont T11, T14, T18, T24, T32, T44;

static void loadFonts() {
    static lgfx::PointerWrapper data[6];
    const uint8_t* const SRC[] = {VLW_T11, VLW_T14, VLW_T18, VLW_T24, VLW_T32, VLW_T44};
    lgfx::VLWfont* const FONT[] = {&T11, &T14, &T18, &T24, &T32, &T44};
    for (int i = 0; i < 6; i++) {
        data[i].set(SRC[i]);
        if (!FONT[i]->loadFont(&data[i])) Serial.printf("# font %d did not load\n", i);
    }
}

static void scanlines() {
    canvas.fillScreen(C_BG);
    for (int y = 1; y < canvas.height(); y += 3) canvas.drawFastHLine(0, y, canvas.width(), C_SCAN);
}

// A panel with two corners cut off, the shape everything here is made of
static void chamfer(int x, int y, int w, int h, int c, uint16_t color, bool fill) {
    auto& d = canvas;
    if (fill) {
        d.fillRect(x, y + c, w, h - 2 * c, color);
        d.fillRect(x + c, y, w - c, c, color);
        d.fillRect(x, y + h - c, w - c, c, color);
        d.fillTriangle(x, y + c, x + c, y, x + c, y + c, color);
        d.fillTriangle(x + w - c - 1, y + h - c - 1, x + w - 1, y + h - c - 1, x + w - c - 1, y + h - 1, color);
    } else {
        d.drawLine(x + c, y, x + w - 1, y, color);
        d.drawLine(x + w - 1, y, x + w - 1, y + h - c - 1, color);
        d.drawLine(x + w - 1, y + h - c - 1, x + w - c - 1, y + h - 1, color);
        d.drawLine(x + w - c - 1, y + h - 1, x, y + h - 1, color);
        d.drawLine(x, y + h - 1, x, y + c, color);
        d.drawLine(x, y + c, x + c, y, color);
    }
}

// HUD corner marks around a box
static void brackets(int x, int y, int w, int h, uint16_t color) {
    auto& d = canvas;
    const int L = 7;
    d.drawFastHLine(x, y, L, color), d.drawFastVLine(x, y, L, color);
    d.drawFastHLine(x + w - L, y, L, color), d.drawFastVLine(x + w - 1, y, L, color);
    d.drawFastHLine(x, y + h - 1, L, color), d.drawFastVLine(x, y + h - L, L, color);
    d.drawFastHLine(x + w - L, y + h - 1, L, color), d.drawFastVLine(x + w - 1, y + h - L, L, color);
}

static void drawRaw(const char* text, int x, int y, uint8_t datum, uint16_t color, const lgfx::IFont* font = &T14) {
    canvas.setFont(font);
    canvas.setTextSize(1);
    canvas.setTextDatum(datum);
    canvas.setTextColor(color);
    canvas.drawString(text, x, y);
}

// Big digits, shrunk to fit: the clock
static void drawBig(const char* text, int x, int y, uint16_t color, int width) {
    auto& d = canvas;
    d.setFont(&T44);
    d.setTextSize(1);
    int w = d.textWidth(text);
    if (w > width) d.setTextSize((float)width / w);
    d.setTextDatum(middle_center);
    d.setTextColor(color);
    d.drawString(text, x, y);
    d.setTextSize(1);
}

static int battery() { return M5.Power.getBatteryLevel(); }

static void drawBattery(int right, int y) {
    int level = battery();
    if (level < 0) return;
    auto& d = canvas;
    const int SEG = 5, W = 4, GAP = 1;
    int x = right - SEG * (W + GAP);
    uint16_t fill = level <= 15 ? C_RED : level <= 35 ? C_YELLOW : C_ACCENT;
    int lit = (constrain(level, 0, 100) + 19) / 20;
    for (int i = 0; i < SEG; i++) d.fillRect(x + i * (W + GAP), y, W, 9, i < lit ? fill : C_DIM);
    char text[8];
    snprintf(text, sizeof text, M5.Power.isCharging() ? "+%d%%" : "%d%%", level);
    drawRaw(text, x - 3, y + 5, middle_right, C_TEXT);
}

static uint16_t linkColor() {
    bool q = questConnected(), l = laptopConnected();
    return q && l ? C_GREEN : q || l ? C_YELLOW : C_MUTED;
}

static void clockText(char* out, size_t size, bool seconds);

// [12:34] //NEW VISION ............ link  battery   (no number: a clock in the tag; DEBUG keeps its name)
static void drawHeader(const char* name, int number) {
    auto& d = canvas;
    scanlines();
    char tag[12];
    if (number == 1) clockText(tag, sizeof tag, false);
    else snprintf(tag, sizeof tag, "%02d", number);
    d.setFont(&T14);
    int tagWidth = d.textWidth(tag) + 12;
    chamfer(0, 0, tagWidth, 15, 4, C_ACCENT, true);
    drawRaw(tag, tagWidth / 2, 8, middle_center, C_BLACK);
    int brandAt = tagWidth + 5;
    if (number != 1) {
        drawRaw(name, brandAt, 8, middle_left, C_ACCENT);
        brandAt += d.textWidth(name) + 3;
    }
    d.setFont(&T11);
    if (brandAt + d.textWidth("//NEW VISION") < d.width() - 90) drawRaw("//NEW VISION", brandAt, 9, middle_left, C_DIM, &T11);
    d.fillRect(d.width() - 85, 4, 7, 7, linkColor());
    drawBattery(d.width() - 2, 3);
    d.drawFastHLine(0, 16, d.width(), C_DIM);
    d.drawFastHLine(0, 16, 60, C_ACCENT);
}

// A short burst of torn lines and colour fringes after the screen changes
static unsigned long glitchUntil = 0;
static void glitch(unsigned long t = millis()) { if (fxLevel) glitchUntil = t + 220; }
static bool due(unsigned long until, unsigned long t) { return until && (long)(until - t) > 0; }
static void drawGlitch(unsigned long t) {
    if (!due(glitchUntil, t)) return;
    auto& d = canvas;
    for (int i = 0; i < 5; i++) {
        int y = esp_random() % d.height(), h = 1 + esp_random() % 3, x = esp_random() % d.width();
        d.fillRect(x, y, 20 + esp_random() % 120, h, i % 2 ? C_CYAN : C_ACCENT);
    }
    int y = 20 + esp_random() % 80;
    d.fillRect(0, y, d.width(), 2, C_BLACK);
}

static float easeOut(float p) { return 1 - (1 - p) * (1 - p) * (1 - p); }

// Letters not yet decoded show as noise, resolving left to right
static void decode(const char* src, char* out, size_t size, float progress) {
    static const char NOISE[] = "#$%&*+<>/|=01ABCDEFXZ";
    size_t letters = strlen(src);
    size_t shown = progress >= 1 ? letters : (size_t)(letters * progress), o = 0;
    for (size_t i = 0; i < letters && o + 1 < size; i++) out[o++] = i < shown || src[i] == ' ' ? src[i] : NOISE[esp_random() % (sizeof NOISE - 1)];
    out[o] = 0;
}

// An old monitor switching on: a line grows from the centre, then the picture opens up and down. p 0..1
static void crtOpen(float p) {
    auto& d = canvas;
    int W = d.width(), H = d.height();
    if (p < 0.35f) {
        int w = max(2, (int)(W * easeOut(p / 0.35f)));
        d.fillScreen(C_BLACK);
        d.fillRect((W - w) / 2, H / 2 - 1, w, 2, C_TEXT);
        d.drawFastHLine((W - w) / 2, H / 2 - 2, w, C_ACCENT);
        d.drawFastHLine((W - w) / 2, H / 2 + 1, w, C_ACCENT);
        return;
    }
    int h = max(4, (int)(H * easeOut((p - 0.35f) / 0.65f))), top = (H - h) / 2;
    d.fillRect(0, 0, W, top, C_BLACK);
    d.fillRect(0, top + h, W, H - top - h, C_BLACK);
    d.drawFastHLine(0, top, W, C_ACCENT);
    d.drawFastHLine(0, top + h - 1, W, C_ACCENT);
}

// ======================================================================== Impact FX (after Vartovyi)
// A knock leaves the screen "broken" for a moment: rows torn sideways, stuck pixel columns, noise, and on a hard hit
// a flash of inverted colours. The picture also feels the motion: it lags behind a swing on a spring.
// On the headset the thresholds are low: a quick nod or shake of the head is enough.

enum Knock { K_NONE, K_SWING, K_STRIKE, K_DROP };
struct KnockSense {
    unsigned long fallSince, fellAt, lastAt;
    bool fell;
};
static const float FREEFALL_G = 0.35f, LANDING_G = 2.0f, SWING_G = 1.3f, STRIKE_LOW_G = 2.4f, STRIKE_HIGH_G = 1.9f;
static const unsigned long FREEFALL_MS = 80, LAND_WITHIN_MS = 1000, KNOCK_GAP_MS = 300;

static Knock senseKnock(KnockSense& s, float g, unsigned long t, int level) {
    if (!level) return K_NONE;
    if (g < FREEFALL_G) {
        if (!s.fallSince) s.fallSince = t;
        if (t - s.fallSince >= FREEFALL_MS) s.fell = true, s.fellAt = t;
        return K_NONE;
    }
    s.fallSince = 0;
    if (s.fell && t - s.fellAt > LAND_WITHIN_MS) s.fell = false;
    Knock k = K_NONE;
    if (s.fell && g >= LANDING_G) k = K_DROP;
    else if (g >= (level == 2 ? STRIKE_HIGH_G : STRIKE_LOW_G)) k = K_STRIKE;
    else if (level == 2 && g >= SWING_G) k = K_SWING;
    if (k == K_NONE) return K_NONE;
    if (k != K_DROP && s.lastAt && t - s.lastAt < KNOCK_GAP_MS) return K_NONE;
    s.fell = false;
    s.lastAt = t;
    return k;
}

struct Spring {
    float x, v;
    void step(float dt, float limit) {
        const float STIFF = 360, DAMP = 9;
        v += (-STIFF * x - DAMP * v) * dt;
        x += v * dt;
        if (x > limit) x = limit, v = 0;
        if (x < -limit) x = -limit, v = 0;
    }
    bool moving() const { return x > 0.5f || x < -0.5f || v > 5 || v < -5; }
};

enum Side { SIDE_LEFT, SIDE_RIGHT, SIDE_TOP, SIDE_BOTTOM };
static Side hitSide(float right, float down) {
    if (fabsf(right) >= fabsf(down)) return right > 0 ? SIDE_LEFT : SIDE_RIGHT;
    return down > 0 ? SIDE_TOP : SIDE_BOTTOM;
}

static const unsigned long FX_MS[] = {0, 900, 2500, 2300}, INVERT_MS = 130, WAVE_MS = 450;
static KnockSense knock{};
static Knock fxKind = K_NONE;
static unsigned long fxAt = 0;
static uint32_t fxSeed = 1;
static bool inverted = false;
static Side waveFrom = SIDE_LEFT;
static Spring shiftX{}, shiftY{}, skew{};
static float gravX = 0, gravY = 0, gravZ = 1;

// Sensor axes on the screen (Vartovyi's measurement); a flipped picture flips them too
#define AXIS_RIGHT(ax, ay) (flipped ? -(ay) : (ay))
#define AXIS_DOWN(ax, ay) (flipped ? -(ax) : (ax))

static uint32_t fxRand(uint32_t& s) {
    s ^= s << 13, s ^= s >> 17, s ^= s << 5;
    return s;
}

static float fxLevelNow(unsigned long t) {
    if (fxKind == K_NONE) return 0;
    unsigned long e = t - fxAt;
    if (e >= FX_MS[fxKind]) return fxKind = K_NONE, 0;
    return (fxKind == K_SWING ? 0.5f : 1.0f) * (1 - e / (float)FX_MS[fxKind]);
}

static bool fxMoving(unsigned long t) { return fxKind != K_NONE || shiftX.moving() || shiftY.moving() || skew.moving(); }

static void scrollArea(int x, int y, int w, int h, int dx, int dy) {
    int W = canvas.width(), H = canvas.height();
    if (x < 0) w += x, x = 0;
    if (y < 0) h += y, y = 0;
    w = min(w, W - x), h = min(h, H - y);
    if ((!dx && !dy) || w <= 0 || h <= 0) return;
    canvas.setScrollRect(x, y, w, h);
    canvas.scroll(dx, dy);
}

static void drawMotion() {
    auto& d = canvas;
    int W = d.width(), H = d.height();
    d.setBaseColor(C_BLACK);
    int top = (int)skew.x;
    if (top) for (int y = 0; y < H; y += 5) scrollArea(0, y, W, 5, top * (H / 2 - y) / (H / 2), 0);
    int dx = (int)shiftX.x, dy = (int)shiftY.x;
    scrollArea(0, 0, W, H, dx, dy);
    d.clearScrollRect();
    if (dx > 1) d.drawFastVLine(dx - 1, 0, H, C_CYAN);
    if (dx < -1) d.drawFastVLine(W + dx, 0, H, C_MAGENTA);
    if (dy > 1) d.drawFastHLine(0, dy - 1, W, C_CYAN);
    if (dy < -1) d.drawFastHLine(0, H + dy, W, C_MAGENTA);
}

static void drawImpact(unsigned long t) {
    auto& d = canvas;
    int W = d.width(), H = d.height();
    drawMotion();
    float k = fxLevelNow(t);
    if (k <= 0) return;
    unsigned long e = t - fxAt;
    const uint16_t BROKEN[] = {C_CYAN, C_MAGENTA, C_GREEN, C_WHITE};
    d.setBaseColor(C_BLACK);
    int lean = shiftX.v > 0 ? 1 : -1;
    for (int i = 0, n = 1 + (int)(k * 6); i < n; i++) {
        int y = esp_random() % H, h = 2 + esp_random() % (3 + (int)(12 * k));
        scrollArea(0, y, W, h, lean * (int)(esp_random() % (1 + (int)(30 * k))), 0);
    }
    if (fxKind >= K_STRIKE && e < WAVE_MS) {
        float p = e / (float)WAVE_MS;
        bool across = waveFrom == SIDE_LEFT || waveFrom == SIDE_RIGHT;
        int span = across ? W : H, at = (int)(p * span);
        if (waveFrom == SIDE_RIGHT || waveFrom == SIDE_BOTTOM) at = span - at;
        for (int i = 0; i < 6; i++) {
            int off = at - 12 + (int)(esp_random() % 24), len = 4 + esp_random() % 14, jolt = (int)(esp_random() % 17) - 8;
            if (across) scrollArea(off, 0, len, H, 0, jolt);
            else scrollArea(0, off, W, len, jolt, 0);
        }
        d.clearScrollRect();
        for (int i = 0; i < 10; i++) {
            int a = at - 10 + (int)(esp_random() % 20), b = esp_random() % (across ? H : W);
            if (across) d.fillRect(a, b, 2 + esp_random() % 5, 1 + esp_random() % 6, BROKEN[esp_random() % 4]);
            else d.fillRect(b, a, 1 + esp_random() % 6, 2 + esp_random() % 5, BROKEN[esp_random() % 4]);
        }
    }
    d.clearScrollRect();
    uint32_t s = fxSeed;
    for (int i = 0, n = (int)(k * 5 + 0.5f); i < n; i++) d.drawFastVLine(fxRand(s) % W, 0, H, BROKEN[fxRand(s) % 4]);
    for (int i = 0, n = (int)(k * 12); i < n; i++)
        d.fillRect(esp_random() % W, esp_random() % H, 2 + esp_random() % 10, 1 + esp_random() % 4, BROKEN[esp_random() % 4]);
    if (fxKind == K_DROP && e > 250) {
        chamfer(28, 46, W - 56, 42, 6, C_BLACK, true);
        chamfer(28, 46, W - 56, 42, 6, C_RED, false);
        drawRaw("IMPACT DETECTED", W / 2, 59, middle_center, C_RED);
        char line[24];
        snprintf(line, sizeof line, "RECALIBRATING%.*s", (int)(e / 250 % 4), "...");
        drawRaw(line, W / 2, 76, middle_center, C_ACCENT);
    }
}

static void senseImpact(unsigned long t) {
    static unsigned long last = 0;
    if (!fxLevel || screenMode == SCREEN_OFF) {
        shiftX = shiftY = skew = Spring{};
        fxKind = K_NONE;
        last = t;
        return;
    }
    if (t - last < 20) return;
    float dt = min(0.05f, (t - last) / 1000.0f);
    last = t;
    float ax = 0, ay = 0, az = 0, rx = 0, ry = 0, rz = 0;
    if (!M5.Imu.getAccel(&ax, &ay, &az)) return;
    M5.Imu.getGyro(&rx, &ry, &rz);
    float g = sqrtf(ax * ax + ay * ay + az * az);
    if (g > 0.85f && g < 1.15f) gravX += (ax - gravX) * 0.08f, gravY += (ay - gravY) * 0.08f, gravZ += (az - gravZ) * 0.08f;
    float right = AXIS_RIGHT(ax - gravX, ay - gravY), down = AXIS_DOWN(ax - gravX, ay - gravY);
    float floorRight = AXIS_RIGHT(gravX, gravY), floorDown = AXIS_DOWN(gravX, gravY);
    Knock k = senseKnock(knock, g, t, fxLevel);
    bool falling = knock.fallSince && t - knock.fallSince >= FREEFALL_MS;
    // The picture lags behind the motion. On LOW only knocks move it, on HIGH even a light shake does
    bool brisk = fxLevel == 2 && right * right + down * down > 0.02f;
    if (brisk || k != K_NONE) {
        float push = 2500 * dt * (k != K_NONE ? 2 : 1);
        shiftX.v -= right * push;
        shiftY.v -= down * push;
    }
    float twist = fabsf(rz) > fabsf(rx) ? rz : rx;   // on the headset a head turn shows on either axis
    if (fxLevel == 2 && fabsf(twist) > 120) skew.v += twist * 0.5f * dt * 50;
    if (falling) shiftX.v -= floorRight * 400 * dt, shiftY.v -= floorDown * 400 * dt;
    if (k == K_DROP) shiftX.v += floorRight * 900, shiftY.v += floorDown * 900;
    shiftX.step(dt, 40);
    shiftY.step(dt, 30);
    skew.step(dt, 30);
    if (k == K_NONE || (fxLevelNow(t) > 0 && k < fxKind)) return;
    if (k >= K_STRIKE) waveFrom = hitSide(right, down);
    fxKind = k, fxAt = t, fxSeed = esp_random() | 1;
}

// ======================================================================== screens

static bool clockKnown();

static void logLine(const String& text) {
    char stamp[16];
    if (clockKnown()) {   // real time once the headset has sent it, uptime mm:ss before that
        clockText(stamp, sizeof stamp, true);
        strcat(stamp, " ");
    } else {
        uint32_t s = millis() / 1000;
        snprintf(stamp, sizeof(stamp), "%02lu:%02lu ", (unsigned long)(s / 60 % 100), (unsigned long)(s % 60));
    }
    String line = String(stamp) + text;
    if (logCount < LOG_LINES) {
        logLines[logCount++] = line;
    } else {
        for (int i = 1; i < LOG_LINES; i++) logLines[i - 1] = logLines[i];
        logLines[LOG_LINES - 1] = line;
    }
    Serial.println("# " + text);
}

static bool clockKnown() { return epochBase > 1700000000LL; }

static void clockText(char* out, size_t size, bool seconds) {
    if (!clockKnown()) {
        snprintf(out, size, seconds ? "--:--:--" : "--:--");
        return;
    }
    time_t now = (time_t)(epochBase + (millis() - epochAtMs) / 1000 + tzMinutes * 60);
    struct tm local;
    gmtime_r(&now, &local);
    strftime(out, size, seconds ? "%H:%M:%S" : "%H:%M", &local);
}

// A line of status that runs right to left
static void drawTicker(int y) {
    auto& d = canvas;
    char text[160];
    snprintf(text, sizeof text, "LAST %s  //  BAT %d%%  //  FX %s  //  %s  //  UP %luM  //  ", lastEvent.c_str(), battery(),
             FX_NAMES[fxLevel], THEMES[themeIndex].name, (unsigned long)(millis() / 60000));
    d.setFont(&T11);
    d.setTextSize(1);
    int w = d.textWidth(text), shift = (millis() / 45) % max(1, w);
    d.setClipRect(0, y - 6, d.width(), 13);
    for (int x = -shift; x < d.width(); x += w) drawRaw(text, x, y, middle_left, C_MUTED, &T11);
    d.clearClipRect();
}

static void drawPill(int x, int y, int w, bool good, const char* label) {
    uint16_t color = good ? C_GREEN : C_YELLOW;
    chamfer(x, y, w, 13, 3, color, false);
    canvas.fillRect(x + 5, y + 4, 5, 5, color);
    drawRaw(label, x + 14, y + 7, middle_left, color, &T11);
}

// The last events in a HUD frame: newest at the bottom in accent, older ones fade
static void drawHome() {
    auto& d = canvas;
    drawHeader("HOME", 1);
    const int ROWS = 7, TOP = 20, ROW_H = 12;
    brackets(2, TOP - 1, d.width() - 4, ROWS * ROW_H + 4, C_DIM);
    int first = max(0, logCount - ROWS);
    for (int i = first; i < logCount; i++) {
        int row = i - first + (ROWS - min(logCount, ROWS));   // stick to the bottom like a terminal
        bool newest = i == logCount - 1;
        uint16_t color = newest ? C_ACCENT : row >= ROWS - 3 ? C_TEXT : C_MUTED;
        String line = logLines[i];
        line.toUpperCase();
        int y = TOP + 2 + row * ROW_H;
        if (newest) d.fillRect(6, y + 1, 3, 9, C_ACCENT);
        drawRaw(line.c_str(), 13, y + 5, middle_left, color, &T11);
    }
    int half = (d.width() - 8 - 4) / 2;
    drawPill(4, 107, half, questConnected(), questConnected() ? "QUEST" : "NO QUEST");
    drawPill(8 + half, 107, half, laptopConnected(), laptopConnected() ? "LAPTOP" : "NO LAPTOP");
    drawTicker(128);
}

static void drawDebug() {
    auto& d = canvas;
    drawHeader("DEBUG", 2);
    for (int i = 0; i < logCount; i++) drawRaw(logLines[i].c_str(), 4, 25 + i * 11, middle_left, C_TEXT, &T11);
    d.drawFastHLine(0, 117, d.width(), C_DIM);
    char line[64];
    snprintf(line, sizeof line, "%s  //  CONN %d  //  MSG %lu  //  HEAP %luK", FW_VERSION, connections, (unsigned long)packets,
             (unsigned long)(ESP.getFreeHeap() / 1024));
    drawRaw(line, 4, 126, middle_left, C_MUTED, &T11);
}

static unsigned long lastDraw = 0;

static void render(unsigned long t) {
    if (screenMode == SCREEN_OFF) return;
    bool invert = fxKind >= K_STRIKE && t - fxAt < INVERT_MS;
    if (invert != inverted) M5.Display.invertDisplay(inverted = invert);
    bool moving = due(glitchUntil, t) || fxMoving(t);
    if (t - lastDraw < (moving ? 30UL : 90UL)) return;
    lastDraw = t;
    if (screenMode == SCREEN_DEBUG) drawDebug(); else drawHome();
    drawGlitch(t);
    drawImpact(t);
    canvas.pushSprite(0, 0);
}

// Power on: the monitor opens, a boot log runs, the name decodes out of noise
static void bootAnimation() {
    auto& d = canvas;
    const unsigned long OPEN = 600, STEP = 260, LOG = 2000, LOGO_FOR = 1800, LOGO = LOG + LOGO_FOR, DECODE = 800;
    const char* const LINES[][2] = {{"> core", "OK"}, {"> display", "OK"}, {"> imu", "OK"}, {"> ble", "ARMED"},
                                    {"> uplink", "QUEST // LAPTOP"}};
    const int N = sizeof LINES / sizeof LINES[0];
    unsigned long start = millis();
    for (unsigned long e = 0; e < LOGO; e = millis() - start) {
        scanlines();
        if (e < LOGO - LOGO_FOR) {
            drawRaw("NEW VISION // HEADSET TERMINAL", 6, 10, middle_left, C_ACCENT, &T11);
            d.drawFastHLine(6, 17, d.width() - 12, C_DIM);
            int shown = e < OPEN ? 0 : min(N, (int)((e - OPEN) / STEP) + 1);
            for (int i = 0; i < shown; i++) {
                int y = 31 + i * 15;
                bool last = i == shown - 1 && e < OPEN + (unsigned long)(i + 1) * STEP;
                drawRaw(LINES[i][0], 6, y, middle_left, C_TEXT);
                d.setFont(&T14);
                int dotsFrom = 6 + d.textWidth(LINES[i][0]) + 4;
                for (int x = dotsFrom; x < 120; x += 6) d.fillRect(x, y + 3, 2, 2, C_DIM);
                if (!last) drawRaw(LINES[i][1], d.width() - 8, y, middle_right, C_GREEN);
            }
            float bar = e < OPEN ? 0 : min(1.0f, (e - OPEN) / (float)(LOG - OPEN));
            d.drawRect(6, 112, d.width() - 12, 10, C_DIM);
            d.fillRect(8, 114, (int)((d.width() - 16) * bar), 6, C_ACCENT);
        } else {
            float p = min(1.0f, (e - (LOGO - LOGO_FOR)) / (float)DECODE);
            char name[16];
            decode("NEW VISION", name, sizeof name, p);
            brackets(10, 22, d.width() - 20, 76, C_DIM);
            if (p < 1) drawRaw(name, d.width() / 2 + 2, 58, middle_center, C_CYAN, &fonts::Orbitron_Light_24);
            drawRaw(name, d.width() / 2, 56, middle_center, C_ACCENT, &fonts::Orbitron_Light_24);
            drawRaw(FW_VERSION, d.width() / 2, 88, middle_center, C_MUTED);
            glitchUntil = p < 1 ? millis() + 40 : 0;
            drawGlitch(millis());
        }
        if (e < OPEN) crtOpen(e / (float)OPEN);
        d.pushSprite(0, 0);
        delay(30);
    }
    glitch();
}

static void applyScreen() {
    M5.Display.setBrightness(screenMode == SCREEN_OFF ? 0 : brightness);
    if (screenMode == SCREEN_OFF) M5.Display.fillScreen(C_BLACK);
    lastDraw = 0;
    glitch();
}

// ======================================================================== commands

static void sendKeys(uint8_t modifier, uint8_t key) {
    uint8_t report[8] = {modifier, 0, key, 0, 0, 0, 0, 0};
    keyboardInput->setValue(report, sizeof(report));
    keyboardInput->notify();
    delay(40);
    uint8_t release[8] = {0};
    keyboardInput->setValue(release, sizeof(release));
    keyboardInput->notify();
}

static void sendMedia(uint16_t usage) {
    uint8_t report[2] = {(uint8_t)(usage & 0xFF), (uint8_t)(usage >> 8)};
    mediaInput->setValue(report, sizeof(report));
    mediaInput->notify();
    delay(40);
    uint8_t release[2] = {0, 0};
    mediaInput->setValue(release, sizeof(release));
    mediaInput->notify();
}

static void runCommand(const Command& c) {
    for (int i = 0; i < c.repeat; i++) {
        if (i) delay(30);
        if (c.media) sendMedia(c.media); else sendKeys(c.modifier, c.key);
    }
}

static const Command* findCommand(const String& name) {
    for (const auto& c : COMMANDS)
        if (name == c.name) return &c;
    return nullptr;
}

static const char* screenName() { return screenMode == SCREEN_DEBUG ? "debug" : screenMode == SCREEN_OFF ? "off" : "home"; }

static String statusLine() {
    return String("STATUS bt=") + (laptopConnected() ? 1 : 0) + " bat=" + battery() + " chg=" + (M5.Power.isCharging() ? 1 : 0) +
           " vol=" + volume + " bri=" + brightness + " scr=" + screenName() + " th=" + themeIndex + " fx=" + fxLevel +
           " clk=" + (clockKnown() ? 1 : 0) + " rot=" + (flipped ? 1 : 0) +
           " up=" + millis() / 1000 + " heap=" + ESP.getFreeHeap();
}

enum Channel { FROM_BLE, FROM_SERIAL };

static void handleLine(String msg, Channel from) {
    auto reply = [from](const String& text) {
        if (from == FROM_BLE) sendBle(text); else Serial.println(text);
    };
    msg.trim();
    if (msg.length() == 0) return;
    packets++;
    if (from == FROM_BLE) {
        if (!lastQuestMs || millis() - lastQuestMs > 5000) {
            logLine("quest talking");
            glitch();
        }
        lastQuestMs = millis();
    }

    if (msg == "NV?") {
        reply(String("NVSTICK bt=") + (laptopConnected() ? "1" : "0"));
        return;
    }
    if (msg == "STATUS") {   // polled every second: not logged
        reply(statusLine());
        return;
    }
    if (msg.startsWith("CMD ")) {
        String name = msg.substring(4);
        const Command* c = findCommand(name);
        if (!laptopConnected()) {
            reply("ERR notpaired");
            logLine("cmd " + name + ": no laptop");
        } else if (c) {
            runCommand(*c);
            reply("OK " + name);
            logLine("cmd " + name + " -> laptop");
            lastEvent = c->label;
            glitch();
            M5.Speaker.tone(2000, 40);
        } else {
            reply("ERR unknown");
            logLine("cmd " + name + ": unknown");
        }
        return;
    }
    if (msg.startsWith("TIME ")) {
        int space = msg.indexOf(' ', 5);
        epochBase = atoll(msg.substring(5, space > 0 ? space : msg.length()).c_str());
        epochAtMs = millis();
        tzMinutes = space > 0 ? msg.substring(space + 1).toInt() : 0;
        reply("OK time");
        logLine("time synced");
        return;
    }
    if (msg.startsWith("SET ")) {
        int space = msg.indexOf(' ', 4);
        String key = space > 0 ? msg.substring(4, space) : msg.substring(4);
        String value = space > 0 ? msg.substring(space + 1) : "";
        if (key == "vol") {
            volume = constrain(value.toInt(), 0, 255);
            M5.Speaker.setVolume(volume);
            if (volume) M5.Speaker.tone(1500, 30);
        } else if (key == "bri") {
            brightness = constrain(value.toInt(), 0, 255);
            if (screenMode != SCREEN_OFF) M5.Display.setBrightness(brightness);
        } else if (key == "scr") {
            screenMode = value == "off" ? SCREEN_OFF : value == "debug" ? SCREEN_DEBUG : SCREEN_HOME;
            applyScreen();
        } else if (key == "theme") {
            int t = constrain(value.toInt(), 0, 5);
            if (t != themeIndex) {
                themeIndex = t;
                applyTheme();
                glitch();
            }
        } else if (key == "fx") {
            fxLevel = constrain(value.toInt(), 0, 2);
        } else if (key == "rot") {
            flipped = value.toInt() != 0;
            M5.Display.setRotation(flipped ? 3 : 1);
            gravX = -gravX, gravY = -gravY;   // keep "down" right after the turn
            lastDraw = 0;
            glitch();
        } else {
            reply("ERR unknown");
            logLine("set " + key + ": unknown");
            return;
        }
        saveSettings();
        reply("OK " + key);
        if (key != "theme" && key != "fx") logLine("set " + key + " = " + value);   // sent on every link-up: not logged
        return;
    }
    if (msg.startsWith("LOG ")) {   // an event from the headset for the stick's log, e.g. "FOUND LAPTOP"
        String text = msg.substring(4, min((int)msg.length(), 40));
        logLine(text);
        lastEvent = text;
        lastEvent.toUpperCase();
        glitch();
        reply("OK log");
        return;
    }
    if (msg == "BEEP") {
        reply("OK beep");
        logLine("beep");
        lastEvent = "BEEP";
        glitch();
        M5.Speaker.tone(2400, 120);
        delay(160);
        M5.Speaker.tone(3200, 120);
        return;
    }
    if (msg == "REBOOT") {
        reply("OK reboot");
        logLine("reboot");
        delay(300);
        ESP.restart();
    }
    if (msg == "OFF") {
        reply("OK off");
        logLine("power off");
        delay(300);
        M5.Power.powerOff();   // while USB powers it, the stick may stay on: then it just goes dark
    }
    reply("ERR unknown");
    logLine("? " + msg.substring(0, 24));
}

static void handleSerial() {
    while (Serial.available()) {
        char c = (char)Serial.read();
        if (c == '\n' || c == '\r') {
            if (serialLine.length()) handleLine(serialLine, FROM_SERIAL);
            serialLine = "";
        } else if (serialLine.length() < 120) {
            serialLine += c;
        }
    }
}

static void handleBleInbox() {
    for (int i = 0; i < 8; i++) {
        std::string line;
        portENTER_CRITICAL(&inboxLock);
        bool has = !inbox.empty();
        if (has) {
            line = inbox.front();
            inbox.pop_front();
        }
        portEXIT_CRITICAL(&inboxLock);
        if (!has) return;
        handleLine(String(line.c_str()), FROM_BLE);
    }
}

// ======================================================================== setup / loop

static void setupBle() {
    NimBLEDevice::init(DEVICE_NAME);
    NimBLEDevice::setMTU(185);
    NimBLEDevice::setSecurityAuth(true, false, true);   // bonding (the laptop pairs), no PIN, secure connections
    NimBLEDevice::setSecurityIOCap(BLE_HS_IO_NO_INPUT_OUTPUT);

    server = NimBLEDevice::createServer();
    server->setCallbacks(new ServerCallbacks());

    // Keyboard for the laptop
    hid = new NimBLEHIDDevice(server);
    keyboardInput = hid->inputReport(1);
    mediaInput = hid->inputReport(2);
    hid->manufacturer()->setValue("New Vision");
    hid->pnp(0x02, 0xe502, 0xa111, 0x0210);
    hid->hidInfo(0x00, 0x01);
    hid->reportMap((uint8_t*)REPORT_MAP, sizeof(REPORT_MAP));
    hid->startServices();
    hid->setBatteryLevel(100);

    // Command channel for the headset (no encryption: the Quest app connects without pairing)
    NimBLEService* uart = server->createService(UART_SERVICE);
    NimBLECharacteristic* rx = uart->createCharacteristic(UART_RX, NIMBLE_PROPERTY::WRITE | NIMBLE_PROPERTY::WRITE_NR);
    rx->setCallbacks(new RxCallbacks());
    uartTx = uart->createCharacteristic(UART_TX, NIMBLE_PROPERTY::NOTIFY);
    uart->start();

    // Advertising: flags + keyboard appearance + HID service + name (fits in 31 bytes)
    NimBLEAdvertising* adv = NimBLEDevice::getAdvertising();
    NimBLEAdvertisementData data;
    data.setFlags(BLE_HS_ADV_F_DISC_GEN | BLE_HS_ADV_F_BREDR_UNSUP);
    data.setAppearance(HID_KEYBOARD);
    data.setCompleteServices16({hid->hidService()->getUUID()});
    data.setName(DEVICE_NAME);
    adv->setAdvertisementData(data);
    NimBLEAdvertisementData scan;
    scan.setCompleteServices({NimBLEUUID(UART_SERVICE)});
    adv->setScanResponseData(scan);
    adv->start();
}

void setup() {
    auto cfg = M5.config();
    M5.begin(cfg);
    Serial.begin(115200);
    loadSettings();
    applyTheme();
    M5.Display.setRotation(flipped ? 3 : 1);   // landscape, 240x135 (3 = turned 180°)
    M5.Display.setBrightness(screenMode == SCREEN_OFF ? 0 : brightness);
    M5.Speaker.setVolume(volume);
    canvas.setPsram(true);
    canvas.createSprite(M5.Display.width(), M5.Display.height());
    loadFonts();

    logLine(String(FW_VERSION) + " boot");
    setupBle();   // advertising starts before the boot animation: the laptop reconnects meanwhile
    logLine("ble advertising");
    if (screenMode != SCREEN_OFF) bootAnimation();
}

void loop() {
    M5.update();
    unsigned long t = millis();

    static bool lastLaptop = false;
    static bool lastQuest = false;
    bool laptop = laptopConnected();
    bool quest = questConnected();
    if (laptop != lastLaptop) {
        lastLaptop = laptop;
        logLine(laptop ? "laptop connected" : "laptop lost");
        lastEvent = laptop ? "LAPTOP LINK" : "LAPTOP LOST";
        glitch(t);
    }
    if (quest != lastQuest) {
        lastQuest = quest;
        logLine(quest ? "quest connected" : "quest lost");
        lastEvent = quest ? "QUEST LINK" : "QUEST LOST";
        glitch(t);
    }

    handleBleInbox();
    handleSerial();
    senseImpact(t);
    render(t);
    delay(2);
}
