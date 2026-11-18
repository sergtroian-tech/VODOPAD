// ============================================================================
//  «Водопад» — прошивка для LED-матрицы с датчиком движения
//  Только водопад: вода переливается по наклону, при встряхивании — брызги.
//  Настраивается программой Vodopad.exe по USB (текстовые команды, 115200).
// ============================================================================
#include <Arduino.h>
#include <Wire.h>
#include <Adafruit_NeoPixel.h>
#include "water.h"

#if defined(ESP8266)
  #include <EEPROM.h>
  #include <ESP8266WiFi.h>
  #define CHIP_NAME "esp8266"
#else
  #include <Preferences.h>
  #if CONFIG_IDF_TARGET_ESP32C3
    #define CHIP_NAME "esp32c3"
  #elif CONFIG_IDF_TARGET_ESP32S3
    #define CHIP_NAME "esp32s3"
  #elif CONFIG_IDF_TARGET_ESP32S2
    #define CHIP_NAME "esp32s2"
  #else
    #define CHIP_NAME "esp32"
  #endif
#endif

#define FW_VERSION "1.0"

#if !defined(ESP8266) && ARDUINO_USB_CDC_ON_BOOT
  #define HAS_TWO_PORTS 1      // Serial = родной USB, Serial0 = UART0 (если стоит CH340)
#else
  #define HAS_TWO_PORTS 0
#endif

// ---------- выходы, которые можно проверять (без flash/USB) ----------
#if defined(ESP8266)
static const int8_t OUT_PINS[] = {2, 4, 5, 12, 13, 14, 15, 0};
static const int8_t IN_PINS[]  = {2, 4, 5, 12, 13, 14, 15, 0, 16};
#elif CONFIG_IDF_TARGET_ESP32C3
static const int8_t OUT_PINS[] = {0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 20, 21, 9};
static const int8_t IN_PINS[]  = {0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 20, 21, 9};
#elif CONFIG_IDF_TARGET_ESP32S3
static const int8_t OUT_PINS[] = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 21,
                                  38, 39, 40, 41, 42, 45, 46, 47, 48, 33, 34, 35, 36, 37, 43, 44, 0};
static const int8_t IN_PINS[]  = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 21,
                                  38, 39, 40, 41, 42, 45, 46, 47, 48, 33, 34, 35, 36, 37, 43, 44, 0};
#elif CONFIG_IDF_TARGET_ESP32S2
static const int8_t OUT_PINS[] = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 21,
                                  33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 45, 43, 44, 0};
static const int8_t IN_PINS[]  = {1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 21,
                                  33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 45, 46, 43, 44, 0};
#else  // ESP32
static const int8_t OUT_PINS[] = {2, 4, 5, 12, 13, 14, 15, 18, 19, 21, 22, 23, 25, 26, 27, 32, 33, 16, 17, 0};
static const int8_t IN_PINS[]  = {2, 4, 5, 12, 13, 14, 15, 18, 19, 21, 22, 23, 25, 26, 27, 32, 33, 16, 17, 0,
                                  34, 35, 36, 39};
#endif

// ---------- настройки ----------
#define CFG_MAGIC 0x57464431u
struct Cfg {
  uint32_t magic;
  int8_t   led;          // выход на матрицу (-1 = ещё не найден)
  uint8_t  w, h;         // размер матрицы
  uint8_t  serp;         // змейка
  uint8_t  rot;          // поворот 0..3 (×90°)
  uint8_t  mir;          // зеркало
  uint8_t  order;        // порядок цветов
  uint8_t  bri;          // яркость 1..255
  uint16_t hue;          // оттенок 0..359
  uint8_t  sat;          // насыщенность
  uint8_t  fill;         // сколько воды, %
  uint8_t  speed;        // скорость 1..10
  uint8_t  mode;         // 0 = вода по наклону, 1 = струи водопада
  uint8_t  sens;         // 0 нет, 1 акселерометр I2C, 2 кнопка-датчик тряски
  int8_t   sda, scl;
  uint8_t  addr, kind;
  int8_t   sw;
  uint8_t  splash;       // сила брызг, %
  uint8_t  cal;          // ось откалибрована
  float    up[3], out[3];
};
Cfg cfg;

void cfgDefaults() {
  memset(&cfg, 0, sizeof(cfg));
  cfg.magic = CFG_MAGIC; cfg.led = -1; cfg.w = 16; cfg.h = 16; cfg.serp = 0; cfg.rot = 0; cfg.mir = 0;
  cfg.order = 0; cfg.bri = 40; cfg.hue = 140; cfg.sat = 235; cfg.fill = 55; cfg.speed = 6; cfg.mode = 0;
  cfg.sens = 0; cfg.sda = -1; cfg.scl = -1; cfg.addr = 0; cfg.kind = 0; cfg.sw = -1; cfg.splash = 30;
  cfg.cal = 0; cfg.up[0] = 0; cfg.up[1] = 1; cfg.up[2] = 0; cfg.out[0] = 0; cfg.out[1] = 0; cfg.out[2] = 1;
}

#if defined(ESP8266)
void cfgLoad() {
  EEPROM.begin(512);
  EEPROM.get(0, cfg);
  if (cfg.magic != CFG_MAGIC) cfgDefaults();
}
void cfgSave() { EEPROM.put(0, cfg); EEPROM.commit(); }
#else
Preferences prefs;
void cfgLoad() {
  prefs.begin("vodopad", false);
  size_t n = prefs.getBytesLength("cfg");
  if (n == sizeof(cfg)) prefs.getBytes("cfg", &cfg, sizeof(cfg));
  if (n != sizeof(cfg) || cfg.magic != CFG_MAGIC) cfgDefaults();
}
void cfgSave() { prefs.putBytes("cfg", &cfg, sizeof(cfg)); }
#endif

// ---------- светодиоды ----------
Adafruit_NeoPixel strip(256, -1, NEO_GRB + NEO_KHZ800);
int curPin = -1;
static const neoPixelType ORDERS[6] = {NEO_GRB, NEO_RGB, NEO_BRG, NEO_RBG, NEO_GBR, NEO_BGR};

int LW() { return (cfg.rot & 1) ? cfg.h : cfg.w; }
int LH() { return (cfg.rot & 1) ? cfg.w : cfg.h; }

int mapXY(int x, int y) {
  if (cfg.mir) x = LW() - 1 - x;
  int PW = cfg.w, PH = cfg.h, px, py;
  switch (cfg.rot & 3) {
    case 0:  px = x;          py = y;          break;
    case 1:  px = PW - 1 - y; py = x;          break;
    case 2:  px = PW - 1 - x; py = PH - 1 - y; break;
    default: px = y;          py = PH - 1 - x; break;
  }
  if (cfg.serp && (py & 1)) px = PW - 1 - px;
  return py * PW + px;
}

void ledShow() { if (curPin >= 0) strip.show(); }

void attachLed(int p) {
  if (curPin >= 0 && curPin != p) {      // гасим старый выход и отпускаем его
    strip.clear(); strip.show();
    pinMode(curPin, INPUT);
  }
  if (p != curPin) {
    if (p >= 0) strip.setPin(p);
    curPin = p;
  }
}

void applyStripCfg() {
  int n = cfg.w * cfg.h;
  if (n < 1) n = 1;
  strip.updateType(ORDERS[cfg.order % 6] + NEO_KHZ800);
  strip.updateLength(n);
  strip.setBrightness(cfg.bri);
  attachLed(cfg.led);
}

// ---------- порты ----------
Stream* ports[2];
int nPorts = 1;
int replyPort = 0;
Stream& R() { return *ports[replyPort]; }
bool viaUart() {
#if HAS_TWO_PORTS
  return replyPort == 1;
#else
  return true;
#endif
}

bool psram() {
#if defined(ESP8266)
  return false;
#else
  return psramFound();
#endif
}

bool pinAllowed(int p) {
#if CONFIG_IDF_TARGET_ESP32C3
  if (viaUart() && HAS_TWO_PORTS && (p == 20 || p == 21)) return false;
#elif CONFIG_IDF_TARGET_ESP32S3
  if (viaUart() && HAS_TWO_PORTS && (p == 43 || p == 44)) return false;
  if (psram() && p >= 33 && p <= 37) return false;
#elif CONFIG_IDF_TARGET_ESP32S2
  if (viaUart() && HAS_TWO_PORTS && (p == 43 || p == 44)) return false;
#elif !defined(ESP8266)
  if (psram() && (p == 16 || p == 17)) return false;
#endif
  return true;
}

// ---------- акселерометр ----------
enum { K_NONE = 0, K_MPU, K_BMI160, K_LIS, K_LIS2DW, K_BMA2, K_ADXL, K_MMA845, K_MMA7660, K_QMI, K_LSM6, K_QMA, K_KX, K_MSA };
static const char* KNAMES[] = {"неизвестный", "MPU6050/6500/9250", "BMI160", "LIS3DH/LIS2DH/SC7A20", "LIS2DW12",
                               "BMA250/253/280", "ADXL345", "MMA845x", "MMA7660", "QMI8658", "LSM6DS",
                               "QMA7981/6100", "KXTJ3", "MSA301/DA2xx"};
static const uint8_t SCAN_ADDRS[] = {0x68, 0x69, 0x18, 0x19, 0x53, 0x1D, 0x1C, 0x4C, 0x6A, 0x6B, 0x12, 0x13, 0x0E, 0x0F, 0x26, 0x27, 0x15};

bool i2cW(uint8_t a, uint8_t r, uint8_t v) {
  Wire.beginTransmission(a); Wire.write(r); Wire.write(v);
  return Wire.endTransmission() == 0;
}
bool i2cR(uint8_t a, uint8_t r, uint8_t* buf, uint8_t n) {
  Wire.beginTransmission(a); Wire.write(r);
  if (Wire.endTransmission(false) != 0) return false;
  if (Wire.requestFrom(a, n) != n) return false;
  for (uint8_t i = 0; i < n; ++i) buf[i] = Wire.read();
  return true;
}
int i2cR8(uint8_t a, uint8_t r) { uint8_t b; return i2cR(a, r, &b, 1) ? b : -1; }

uint8_t identify(uint8_t a) {
  int v;
  if (a == 0x68 || a == 0x69) {
    v = i2cR8(a, 0x75);
    if (v == 0x68 || v == 0x70 || v == 0x71 || v == 0x72 || v == 0x73 || v == 0x74 || v == 0x19 || v == 0x98 || v == 0x12 || v == 0x11) return K_MPU;
    if (i2cR8(a, 0x00) == 0xD1) return K_BMI160;
  }
  if (a == 0x18 || a == 0x19) {
    v = i2cR8(a, 0x0F);
    if (v == 0x33 || v == 0x11 || v == 0x32) return K_LIS;
    if (v == 0x44) return K_LIS2DW;
    v = i2cR8(a, 0x00);
    if (v == 0xF8 || v == 0xF9 || v == 0xFA || v == 0xFB) return K_BMA2;
  }
  if (a == 0x53 || a == 0x1D) { if (i2cR8(a, 0x00) == 0xE5) return K_ADXL; }
  if (a == 0x1D || a == 0x1C) {
    v = i2cR8(a, 0x0D);
    if (v == 0x2A || v == 0x1A || v == 0x5A || v == 0x4A || v == 0x3A) return K_MMA845;
  }
  if (a == 0x4C) return K_MMA7660;
  if (a == 0x6A || a == 0x6B) {
    if (i2cR8(a, 0x00) == 0x05) return K_QMI;
    v = i2cR8(a, 0x0F);
    if (v == 0x69 || v == 0x6A || v == 0x6B || v == 0x6C) return K_LSM6;
  }
  if (a == 0x12 || a == 0x13) { v = i2cR8(a, 0x00); if ((v & 0xF0) == 0xE0 || v == 0x90) return K_QMA; }
  if (a == 0x0E || a == 0x0F) { v = i2cR8(a, 0x0F); if (v == 0x35 || v == 0x09) return K_KX; }
  if (a == 0x26 || a == 0x27) { if (i2cR8(a, 0x01) == 0x13) return K_MSA; }
  return K_NONE;
}

void accelInit(uint8_t a, uint8_t k) {
  switch (k) {
    case K_MPU:    i2cW(a, 0x6B, 0x00); delay(10); i2cW(a, 0x1C, 0x00); break;
    case K_BMI160: i2cW(a, 0x7E, 0x11); delay(10); i2cW(a, 0x41, 0x03); break;
    case K_LIS:    i2cW(a, 0x20, 0x57); i2cW(a, 0x23, 0x88); break;
    case K_LIS2DW: i2cW(a, 0x21, 0x04); i2cW(a, 0x25, 0x00); i2cW(a, 0x20, 0x44); break;
    case K_BMA2:   i2cW(a, 0x0F, 0x03); i2cW(a, 0x11, 0x00); break;
    case K_ADXL:   i2cW(a, 0x31, 0x08); i2cW(a, 0x2D, 0x08); break;
    case K_MMA845: i2cW(a, 0x2A, 0x00); i2cW(a, 0x0E, 0x00); i2cW(a, 0x2A, 0x01); break;
    case K_MMA7660:i2cW(a, 0x07, 0x00); i2cW(a, 0x08, 0x00); i2cW(a, 0x07, 0x01); break;
    case K_QMI:    i2cW(a, 0x02, 0x40); i2cW(a, 0x03, 0x06); i2cW(a, 0x08, 0x01); break;
    case K_LSM6:   i2cW(a, 0x10, 0x40); break;
    case K_QMA:    i2cW(a, 0x36, 0xB6); delay(5); i2cW(a, 0x36, 0x00); i2cW(a, 0x0F, 0x01); i2cW(a, 0x11, 0x80); break;
    case K_KX:     i2cW(a, 0x1B, 0x00); i2cW(a, 0x1B, 0xC0); break;
    case K_MSA:    i2cW(a, 0x0F, 0x00); i2cW(a, 0x10, 0x07); i2cW(a, 0x11, 0x00); break;
  }
  delay(20);
}

static inline int16_t le16(const uint8_t* b) { return (int16_t)(b[0] | (b[1] << 8)); }
static inline int16_t be16(const uint8_t* b) { return (int16_t)((b[0] << 8) | b[1]); }

bool accelRead(float* g) {
  uint8_t b[6]; uint8_t a = cfg.addr;
  float div = 16384.0f; bool be = false; uint8_t reg;
  switch (cfg.kind) {
    case K_MPU:    reg = 0x3B; be = true; break;
    case K_BMI160: reg = 0x12; break;
    case K_LIS:    reg = 0x28 | 0x80; div = 16000.0f; break;
    case K_LIS2DW: reg = 0x28; break;
    case K_BMA2:   reg = 0x02; break;
    case K_ADXL:   reg = 0x32; div = 256.0f; break;
    case K_MMA845: reg = 0x01; be = true; break;
    case K_QMI:    reg = 0x35; break;
    case K_LSM6:   reg = 0x28; div = 16393.0f; break;
    case K_QMA:    reg = 0x01; break;
    case K_KX:     reg = 0x06; break;
    case K_MSA:    reg = 0x02; break;
    case K_MMA7660: {
      if (!i2cR(a, 0x00, b, 3)) return false;
      for (int i = 0; i < 3; ++i) { int v = b[i] & 0x3F; if (v & 0x20) v -= 64; g[i] = v / 21.33f; }
      return true;
    }
    default: return false;
  }
  if (!i2cR(a, reg, b, 6)) return false;
  for (int i = 0; i < 3; ++i) g[i] = (be ? be16(b + 2 * i) : le16(b + 2 * i)) / div;
  return true;
}

bool startI2C(int sda, int scl, uint32_t hz) {
#if defined(ESP8266)
  Wire.begin(sda, scl); Wire.setClock(hz); Wire.setClockStretchLimit(2000);
  return true;
#else
  Wire.end();
  bool ok = Wire.begin(sda, scl, hz);
  Wire.setTimeOut(5);
  return ok;
#endif
}
void stopI2C() {
#if !defined(ESP8266)
  Wire.end();
#endif
}

bool sensorReady = false;
void sensorBegin() {
  sensorReady = false;
  if (cfg.sens == 1 && cfg.sda >= 0 && cfg.scl >= 0) {
    if (startI2C(cfg.sda, cfg.scl, 400000)) {
      uint8_t k = identify(cfg.addr);
      if (k != K_NONE) cfg.kind = k;
      accelInit(cfg.addr, cfg.kind);
      float g[3];
      sensorReady = accelRead(g);
    }
  }
}

// ---------- кнопка/шариковый датчик тряски ----------
volatile uint16_t swCount = 0;
void IRAM_ATTR swIsr() { swCount++; }
int swAttached = -1;
uint8_t swLevel = 1;
void switchBegin() {
  if (swAttached >= 0) { detachInterrupt(digitalPinToInterrupt(swAttached)); swAttached = -1; }
  if (cfg.sens == 2 && cfg.sw >= 0) {
    pinMode(cfg.sw, INPUT_PULLUP);
    swLevel = digitalRead(cfg.sw);
    int irq = digitalPinToInterrupt(cfg.sw);
    if (irq >= 0 && irq != NOT_AN_INTERRUPT) {
      attachInterrupt(irq, swIsr, CHANGE);
      swAttached = cfg.sw;
    }
  }
}
void switchPoll() {            // запасной вариант, если прерывание недоступно
  if (cfg.sens == 2 && cfg.sw >= 0 && swAttached < 0) {
    uint8_t v = digitalRead(cfg.sw);
    if (v != swLevel) { swLevel = v; swCount++; }
  }
}

// ---------- анимация ----------
Water water;
Rain rain;
uint8_t lum[WMAX * WMAX], foam[WMAX * WMAX];
enum { ST_RUN, ST_TRY, ST_ORIENT, ST_FILL, ST_RGB };
int state = ST_RUN;
uint32_t lastFrame = 0, lastSplash = 0;
float gf[2] = {0, 1};          // сглаженная гравитация в координатах матрицы
float lastMag = 1.0f;
float swAcc = 0; uint16_t swLast = 0;

void resetAnim() {
  water.init(LW(), LH(), cfg.fill, millis() + 7);
  rain.init(LW(), LH());
}

void norm3(float* v) { float m = sqrtf(v[0]*v[0] + v[1]*v[1] + v[2]*v[2]); if (m > 1e-6f) { v[0] /= m; v[1] /= m; v[2] /= m; } }
float dot3(const float* a, const float* b) { return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]; }

// up — показание датчика, когда матрица стоит стрелкой вверх;
// out — показание, когда матрица лежит экраном вверх.
void axes(float* u, float* o, float* r) {
  for (int i = 0; i < 3; ++i) { u[i] = cfg.up[i]; o[i] = cfg.out[i]; }
  norm3(u);
  float d = dot3(o, u);
  for (int i = 0; i < 3; ++i) o[i] -= d * u[i];
  norm3(o);
  r[0] = u[1]*o[2] - u[2]*o[1];
  r[1] = u[2]*o[0] - u[0]*o[2];
  r[2] = u[0]*o[1] - u[1]*o[0];
}

bool wantSplash = false;

void readSensors(uint32_t now) {
  float tx, ty;
  if (cfg.sens == 1 && sensorReady) {
    float a[3];
    if (accelRead(a)) {
      float u[3], o[3], r[3]; axes(u, o, r);
      tx = -dot3(a, r);           // вправо
      ty =  dot3(a, u);           // вниз
      float mag = sqrtf(a[0]*a[0] + a[1]*a[1] + a[2]*a[2]);
      if (fabsf(mag - lastMag) > 0.65f || mag > 1.9f) wantSplash = true;
      lastMag = lastMag * 0.6f + mag * 0.4f;
    } else { tx = gf[0]; ty = gf[1]; }
  } else if (cfg.sens == 2) {
    uint16_t c = swCount; uint16_t d = c - swLast; swLast = c;
    swAcc = swAcc * 0.75f + d;
    if (swAcc > 3.0f) { wantSplash = true; swAcc = 0; }
    // без акселерометра вода слегка покачивается
    tx = 0.25f * sinf(now / 1400.0f); ty = 1.0f;
  } else {
    tx = 0.25f * sinf(now / 1400.0f); ty = 1.0f;
  }
  gf[0] = gf[0] * 0.5f + tx * 0.5f;
  gf[1] = gf[1] * 0.5f + ty * 0.5f;
}

uint32_t waterColor(uint8_t l, bool f) {
  uint16_t h = (uint32_t)(cfg.hue % 360) * 65536UL / 360;
  uint8_t s = cfg.sat;
  if (f) { s = s * 6 / 10; h += 1200; }       // гребень волны — светлее
  return strip.gamma32(strip.ColorHSV(h, s, l));
}

void drawFrame() {
  int W = LW(), H = LH();
  for (int y = 0; y < H; ++y)
    for (int x = 0; x < W; ++x) {
      int i = y * W + x;
      strip.setPixelColor(mapXY(x, y), lum[i] ? waterColor(lum[i], foam[i]) : 0);
    }
  ledShow();
}

void runFrame(uint32_t now) {
  readSensors(now);
  if (cfg.mode == 0) {
    if (wantSplash && now - lastSplash > 500) { water.splash(gf[0], gf[1], cfg.splash); lastSplash = now; }
    wantSplash = false;
    float m = sqrtf(gf[0]*gf[0] + gf[1]*gf[1]);
    water.step(gf[0], gf[1]);
    if (m > 0.6f) water.step(gf[0], gf[1]);
    water.render(lum, foam);
  } else {
    int dir;
    if (fabsf(gf[1]) >= fabsf(gf[0])) dir = gf[1] >= 0 ? 0 : 2; else dir = gf[0] >= 0 ? 3 : 1;
    int dens = cfg.fill;
    if (wantSplash && now - lastSplash > 500) { dens = 100; lastSplash = now; }
    wantSplash = false;
    rain.step(dir, dens, cfg.speed, lum, foam);
  }
  drawFrame();
}

void drawOrient() {
  int W = LW(), H = LH();
  strip.clear();
  int cx = W / 2;
  uint32_t white = strip.Color(120, 120, 120);
  for (int y = 1; y < H - 1; ++y) { strip.setPixelColor(mapXY(cx, y), white); if (W > 4) strip.setPixelColor(mapXY(cx - 1, y), white); }
  for (int k = 1; k <= W / 3 && k < H; ++k) {        // наконечник стрелки вверху
    int y = 1 + k;
    if (cx + k < W) strip.setPixelColor(mapXY(cx + k, y), white);
    if (cx - 1 - k >= 0) strip.setPixelColor(mapXY(cx - 1 - k, y), white);
  }
  strip.setPixelColor(mapXY(0, 0), strip.Color(255, 0, 0));          // красный — левый верхний
  strip.setPixelColor(mapXY(W - 1, 0), strip.Color(0, 255, 0));      // зелёный — правый верхний
  strip.setPixelColor(mapXY(0, H - 1), strip.Color(0, 0, 255));      // синий — левый нижний
  ledShow();
}

uint8_t fillRGB[3];
void drawFill() {
  for (int i = 0; i < cfg.w * cfg.h; ++i) strip.setPixelColor(i, strip.Color(fillRGB[0], fillRGB[1], fillRGB[2]));
  ledShow();
}

void drawRGB() {            // слева красный, в центре зелёный, справа синий
  int W = LW(), H = LH();
  for (int y = 0; y < H; ++y)
    for (int x = 0; x < W; ++x) {
      int t = x * 3 / W;
      strip.setPixelColor(mapXY(x, y), t == 0 ? strip.Color(255, 0, 0) : t == 1 ? strip.Color(0, 255, 0) : strip.Color(0, 0, 255));
    }
  ledShow();
}

void redrawStatic() {
  if (state == ST_ORIENT) drawOrient();
  else if (state == ST_FILL) drawFill();
  else if (state == ST_RGB) drawRGB();
}

// ---------- команды ----------
void printCfg() {
  Stream& s = R();
  s.printf("CFG led=%d w=%d h=%d serp=%d rot=%d mir=%d order=%d bri=%d hue=%d sat=%d fill=%d speed=%d mode=%d "
           "sens=%d sda=%d scl=%d addr=%d kind=%d sw=%d splash=%d cal=%d sready=%d chip=%s ver=%s\n",
           cfg.led, cfg.w, cfg.h, cfg.serp, cfg.rot, cfg.mir, cfg.order, cfg.bri, cfg.hue, cfg.sat, cfg.fill,
           cfg.speed, cfg.mode, cfg.sens, cfg.sda, cfg.scl, cfg.addr, cfg.kind, cfg.sw, cfg.splash, cfg.cal,
           sensorReady ? 1 : 0, CHIP_NAME, FW_VERSION);
}

void cmdLedPins() {
  Stream& s = R();
  s.print("PINS");
  for (unsigned i = 0; i < sizeof(OUT_PINS); ++i) if (pinAllowed(OUT_PINS[i])) { s.print(' '); s.print(OUT_PINS[i]); }
  s.print('\n');
}

void cmdTryLed(int p) {
  bool ok = false;
  for (unsigned i = 0; i < sizeof(OUT_PINS); ++i) if (OUT_PINS[i] == p) ok = true;
  if (!ok || !pinAllowed(p)) { R().print("ERR pin\n"); return; }
  state = ST_TRY;
  attachLed(p);
  strip.setBrightness(255);
  for (int i = 0; i < cfg.w * cfg.h; ++i) strip.setPixelColor(i, strip.Color(0, 22, 12));
  strip.show();
  strip.show();
  strip.setBrightness(cfg.bri);
  R().printf("OK TRY %d\n", p);
}

bool inList(int p, const int8_t* l, int n) { for (int i = 0; i < n; ++i) if (l[i] == p) return true; return false; }

void cmdScanI2C() {
  Stream& s = R();
  int8_t cand[48]; int nc = 0;
  for (unsigned i = 0; i < sizeof(OUT_PINS); ++i) {
    int p = OUT_PINS[i];
    if (p == curPin || !pinAllowed(p)) continue;
    cand[nc++] = p;
  }
  // линии I2C обычно подтянуты к питанию снаружи — сначала пробуем такие
  int8_t hi[48]; int nh = 0;
#if !defined(ESP8266)
  for (int i = 0; i < nc; ++i) { pinMode(cand[i], INPUT_PULLDOWN); }
  delay(5);
  for (int i = 0; i < nc; ++i) { if (digitalRead(cand[i])) hi[nh++] = cand[i]; pinMode(cand[i], INPUT); }
#endif
  int8_t* list = nh >= 2 ? hi : cand;
  int nl = nh >= 2 ? nh : nc;
  s.printf("SCAN %d\n", nl);
  int found = 0;
  for (int pass = 0; pass < 2 && !found; ++pass) {
    for (int i = 0; i < nl; ++i)
      for (int j = 0; j < nl; ++j) {
        if (i == j) continue;
        int sda = list[i], scl = list[j];
        pinMode(sda, INPUT_PULLUP); pinMode(scl, INPUT_PULLUP);
        delayMicroseconds(200);
        if (!digitalRead(sda) || !digitalRead(scl)) continue;
        if (!startI2C(sda, scl, 100000)) continue;
        for (unsigned k = 0; k < sizeof(SCAN_ADDRS); ++k) {
          uint8_t a = SCAN_ADDRS[k];
          Wire.beginTransmission(a);
          if (Wire.endTransmission() == 0) {
            uint8_t kind = identify(a);
            s.printf("I2C %d %d %d %d %s\n", sda, scl, a, kind, KNAMES[kind]);
            found++;
          }
        }
        stopI2C();
        pinMode(sda, INPUT); pinMode(scl, INPUT);
        yield();
      }
    if (!found && list == hi) { list = cand; nl = nc; } else break;   // второй проход — все пары
  }
  s.printf("END %d\n", found);
  sensorBegin();
}

void cmdScanSw(int ms) {
  Stream& s = R();
  int8_t pins[48]; int np = 0;
  for (unsigned i = 0; i < sizeof(IN_PINS); ++i) {
    int p = IN_PINS[i];
    if (p == curPin || !pinAllowed(p)) continue;
    if (cfg.sens == 1 && (p == cfg.sda || p == cfg.scl)) continue;
    pins[np++] = p;
  }
  if (swAttached >= 0) { detachInterrupt(digitalPinToInterrupt(swAttached)); swAttached = -1; }
  uint16_t cnt[48]; uint8_t last[48];
  for (int i = 0; i < np; ++i) { pinMode(pins[i], INPUT_PULLUP); cnt[i] = 0; }
  delay(5);
  for (int i = 0; i < np; ++i) last[i] = digitalRead(pins[i]);
  uint32_t t0 = millis();
  while (millis() - t0 < (uint32_t)ms) {
    for (int i = 0; i < np; ++i) {
      uint8_t v = digitalRead(pins[i]);
      if (v != last[i]) { last[i] = v; if (cnt[i] < 60000) cnt[i]++; }
    }
    delayMicroseconds(150);
    if (((millis() - t0) & 63) == 0) yield();
  }
  for (int i = 0; i < np; ++i) if (cnt[i] > 0) s.printf("SW %d %u %d\n", pins[i], cnt[i], last[i]);
  s.print("END\n");
  for (int i = 0; i < np; ++i) pinMode(pins[i], INPUT);
  switchBegin();
}

void cmdCal(bool up) {
  if (!(cfg.sens == 1 && sensorReady)) { R().print("ERR nosensor\n"); return; }
  float acc[3] = {0, 0, 0}, g[3]; int n = 0;
  for (int i = 0; i < 24; ++i) { if (accelRead(g)) { acc[0] += g[0]; acc[1] += g[1]; acc[2] += g[2]; n++; } delay(10); }
  if (!n) { R().print("ERR read\n"); return; }
  float* dst = up ? cfg.up : cfg.out;
  for (int i = 0; i < 3; ++i) dst[i] = acc[i] / n;
  float u[3] = {cfg.up[0], cfg.up[1], cfg.up[2]}, o[3] = {cfg.out[0], cfg.out[1], cfg.out[2]};
  norm3(u); norm3(o);
  cfg.cal = fabsf(dot3(u, o)) < 0.7f ? 1 : 0;
  R().printf("OK CAL %s %.2f %.2f %.2f cal=%d\n", up ? "UP" : "FLAT", dst[0], dst[1], dst[2], cfg.cal);
}

bool setKey(const char* k, long v) {
  #define S(name, lo, hi) if (!strcmp(k, #name)) { if (v < lo || v > hi) return false; cfg.name = v; return true; }
  if (!strcmp(k, "led")) {
    if (v >= 0) { bool ok = false; for (unsigned i = 0; i < sizeof(OUT_PINS); ++i) if (OUT_PINS[i] == v) ok = true; if (!ok) return false; }
    cfg.led = v; return true;
  }
  S(w, 1, WMAX) S(h, 1, WMAX) S(serp, 0, 1) S(rot, 0, 3) S(mir, 0, 1) S(order, 0, 5) S(bri, 1, 255)
  S(hue, 0, 359) S(sat, 0, 255) S(fill, 0, 100) S(speed, 1, 10) S(mode, 0, 1) S(sens, 0, 2)
  S(sda, -1, 48) S(scl, -1, 48) S(addr, 0, 127) S(sw, -1, 48) S(splash, 0, 100)
  #undef S
  return false;
}

void handleLine(char* line) {
  char* cmd = strtok(line, " \t\r");
  if (!cmd) return;
  for (char* c = cmd; *c; ++c) *c = toupper(*c);
  char* a1 = strtok(NULL, " \t\r");
  char* a2 = strtok(NULL, " \t\r");
  char* a3 = strtok(NULL, " \t\r");
  Stream& s = R();
  if (!strcmp(cmd, "PING")) { s.printf("OK VODOPAD %s %s\n", FW_VERSION, CHIP_NAME); }
  else if (!strcmp(cmd, "GET")) printCfg();
  else if (!strcmp(cmd, "LEDPINS")) cmdLedPins();
  else if (!strcmp(cmd, "TRYLED") && a1) cmdTryLed(atoi(a1));
  else if (!strcmp(cmd, "SET") && a1 && a2) {
    for (char* c = a1; *c; ++c) *c = tolower(*c);
    if (!setKey(a1, atol(a2))) { s.print("ERR set\n"); return; }
    if (!strcmp(a1, "led") || !strcmp(a1, "w") || !strcmp(a1, "h") || !strcmp(a1, "order") || !strcmp(a1, "bri")) applyStripCfg();
    if (!strcmp(a1, "w") || !strcmp(a1, "h") || !strcmp(a1, "rot") || !strcmp(a1, "fill") || !strcmp(a1, "mode")) resetAnim();
    if (!strcmp(a1, "sens") || !strcmp(a1, "sda") || !strcmp(a1, "scl") || !strcmp(a1, "addr")) sensorBegin();
    if (!strcmp(a1, "sens") || !strcmp(a1, "sw")) switchBegin();
    redrawStatic();
    s.print("OK\n");
  }
  else if (!strcmp(cmd, "USEI2C") && a1 && a2 && a3) {
    cfg.sens = 1; cfg.sda = atoi(a1); cfg.scl = atoi(a2); cfg.addr = atoi(a3); cfg.kind = 0;
    sensorBegin();
    s.printf("%s %d\n", sensorReady ? "OK SENSOR" : "ERR SENSOR", cfg.kind);
  }
  else if (!strcmp(cmd, "USESW") && a1) {
    cfg.sens = 2; cfg.sw = atoi(a1); switchBegin(); s.print("OK\n");
  }
  else if (!strcmp(cmd, "NOSENSOR")) { cfg.sens = 0; switchBegin(); sensorReady = false; s.print("OK\n"); }
  else if (!strcmp(cmd, "SCANI2C")) cmdScanI2C();
  else if (!strcmp(cmd, "SCANSW")) cmdScanSw(a1 ? constrain(atoi(a1), 500, 15000) : 6000);
  else if (!strcmp(cmd, "ACC")) {
    float g[3];
    if (cfg.sens == 1 && sensorReady && accelRead(g)) s.printf("ACC %.2f %.2f %.2f %.2f %.2f\n", g[0], g[1], g[2], gf[0], gf[1]);
    else s.print("ERR nosensor\n");
  }
  else if (!strcmp(cmd, "CAL") && a1) {
    for (char* c = a1; *c; ++c) *c = toupper(*c);
    cmdCal(!strcmp(a1, "UP"));
  }
  else if (!strcmp(cmd, "ORIENT")) { state = ST_ORIENT; attachLed(cfg.led); drawOrient(); s.print("OK\n"); }
  else if (!strcmp(cmd, "FILL") && a1 && a2 && a3) {
    state = ST_FILL; attachLed(cfg.led);
    fillRGB[0] = atoi(a1); fillRGB[1] = atoi(a2); fillRGB[2] = atoi(a3); drawFill(); s.print("OK\n");
  }
  else if (!strcmp(cmd, "RGBTEST")) { state = ST_RGB; attachLed(cfg.led); drawRGB(); s.print("OK\n"); }
  else if (!strcmp(cmd, "RUN")) { state = ST_RUN; applyStripCfg(); resetAnim(); s.print("OK\n"); }
  else if (!strcmp(cmd, "SPLASH")) { wantSplash = true; s.print("OK\n"); }
  else if (!strcmp(cmd, "SAVE")) { cfgSave(); s.print("OK SAVED\n"); }
  else if (!strcmp(cmd, "DEFAULTS")) { int8_t l = cfg.led; cfgDefaults(); cfg.led = l; applyStripCfg(); resetAnim(); sensorBegin(); switchBegin(); s.print("OK\n"); }
  else if (!strcmp(cmd, "REBOOT")) { s.print("OK\n"); s.flush(); delay(100); ESP.restart(); }
  else s.print("ERR cmd\n");
}

char lineBuf[2][96];
uint8_t lineLen[2];
void pollSerial() {
  for (int p = 0; p < nPorts; ++p) {
    while (ports[p]->available()) {
      int c = ports[p]->read();
      if (c == '\n' || c == '\r') {
        if (lineLen[p]) { lineBuf[p][lineLen[p]] = 0; replyPort = p; handleLine(lineBuf[p]); lineLen[p] = 0; }
      } else if (lineLen[p] < sizeof(lineBuf[0]) - 1) lineBuf[p][lineLen[p]++] = (char)c;
    }
  }
}

void setup() {
#if defined(ESP8266)
  WiFi.mode(WIFI_OFF); WiFi.forceSleepBegin();
#endif
  Serial.begin(115200);
  ports[0] = &Serial; nPorts = 1;
#if HAS_TWO_PORTS
  Serial0.begin(115200);
  ports[1] = &Serial0; nPorts = 2;
#endif
  cfgLoad();
  strip.begin();
  applyStripCfg();
  resetAnim();
  sensorBegin();
  switchBegin();
}

void loop() {
  pollSerial();
  switchPoll();
  uint32_t now = millis();
  if (state == ST_RUN && cfg.led >= 0) {
    uint32_t frameMs = 70 - cfg.speed * 6;      // скорость 1..10 → 64..10 мс
    if (now - lastFrame >= frameMs) { lastFrame = now; runFrame(now); }
  }
  delay(1);
}
