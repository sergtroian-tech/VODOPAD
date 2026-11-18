// Водопад — симуляция воды для LED-матрицы (без зависимостей от Arduino,
// чтобы её можно было проверить на ПК).
#pragma once
#include <stdint.h>
#include <string.h>
#include <math.h>

#ifndef WMAX
#define WMAX 32
#endif

static const int8_t WDX[8] = {1, 1, 0, -1, -1, -1, 0, 1};   // 0=вправо, 2=вниз (y вниз)
static const int8_t WDY[8] = {0, 1, 1, 1, 0, -1, -1, -1};

struct XorRng {
  uint32_t s = 0x9E3779B9u;
  uint32_t next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }
  int range(int n) { return n > 0 ? (int)(next() % (uint32_t)n) : 0; }
  bool chance(int pct) { return range(100) < pct; }
};

// ---------------- Режим 0: жидкость, которая течёт по наклону ----------------
struct Water {
  int W = 16, H = 16;
  uint8_t cell[WMAX * WMAX];      // 0 = пусто, иначе оттенок капли 1..255
  uint8_t moved[WMAX * WMAX];
  uint16_t order[WMAX * WMAX];
  uint8_t stamp = 1;
  int sector = 2;                 // текущее направление «вниз»
  int builtSector = -1;
  XorRng rng;

  void init(int w, int h, int fillPct, uint32_t seed = 1) {
    if (w < 1) w = 1; if (h < 1) h = 1;
    if (w > WMAX) w = WMAX; if (h > WMAX) h = WMAX;
    W = w; H = h;
    if (seed) rng.s ^= seed * 2654435761u;
    memset(cell, 0, sizeof(cell));
    memset(moved, 0, sizeof(moved));
    stamp = 1; builtSector = -1;
    int n = W * H * fillPct / 100;
    if (n > W * H) n = W * H;
    // заливаем снизу вверх
    for (int i = W * H - 1; i >= 0 && n > 0; --i, --n) cell[i] = 170 + rng.range(86);
  }

  int count() const { int c = 0; for (int i = 0; i < W * H; ++i) if (cell[i]) ++c; return c; }

  // Порядок обхода: сначала клетки, которые «ниже» по направлению гравитации.
  void buildOrder(int s) {
    int dx = WDX[s], dy = WDY[s];
    // ключ = x*dx + y*dy, диапазон от -(W+H) до (W+H)
    static uint16_t cnt[4 * WMAX + 4];
    int off = 2 * WMAX + 1;
    memset(cnt, 0, sizeof(cnt));
    for (int y = 0; y < H; ++y)
      for (int x = 0; x < W; ++x) cnt[off - (x * dx + y * dy)]++;   // по убыванию ключа
    uint16_t pos[4 * WMAX + 4];
    uint16_t acc = 0;
    for (int k = 0; k < 4 * WMAX + 4; ++k) { pos[k] = acc; acc += cnt[k]; }
    for (int y = 0; y < H; ++y)
      for (int x = 0; x < W; ++x) order[pos[off - (x * dx + y * dy)]++] = y * W + x;
    builtSector = s;
  }

  inline bool freeAt(int x, int y) const {
    return x >= 0 && y >= 0 && x < W && y < H && cell[y * W + x] == 0;
  }

  static int sectorOf(float gx, float gy) {
    float a = atan2f(gy, gx);
    int s = (int)lroundf(a / 0.78539816f);
    return ((s % 8) + 8) % 8;
  }

  // gx, gy — гравитация в координатах матрицы (x вправо, y вниз), в g.
  void step(float gx, float gy) {
    float m = sqrtf(gx * gx + gy * gy);
    if (m < 0.06f) return;                    // лежит ровно — вода стоит
    int s = sectorOf(gx, gy);
    sector = s;
    if (s != builtSector) buildOrder(s);
    if (++stamp == 0) { memset(moved, 0, sizeof(moved)); stamp = 1; }
    int n = W * H;
    for (int k = 0; k < n; ++k) {
      int idx = order[k];
      if (!cell[idx] || moved[idx] == stamp) continue;
      int x = idx % W, y = idx / W;
      int tries[5];
      tries[0] = s;
      bool flip = rng.next() & 1;
      tries[1] = (s + (flip ? 1 : 7)) & 7;
      tries[2] = (s + (flip ? 7 : 1)) & 7;
      bool flip2 = rng.next() & 1;
      tries[3] = (s + (flip2 ? 2 : 6)) & 7;
      tries[4] = (s + (flip2 ? 6 : 2)) & 7;
      int lim = rng.chance(55) ? 5 : 3;        // растекание в стороны — как у жидкости
      for (int t = 0; t < lim; ++t) {
        int nx = x + WDX[tries[t]], ny = y + WDY[tries[t]];
        if (freeAt(nx, ny)) {
          int ni = ny * W + nx;
          cell[ni] = cell[idx]; cell[idx] = 0; moved[ni] = stamp;
          break;
        }
      }
    }
  }

  // Встряхивание: часть воды подбрасывается вверх (против гравитации) — брызги.
  void splash(float gx, float gy, int pct) {
    int s = sectorOf(gx, gy);
    if (s != builtSector) buildOrder(s);
    int n = W * H;
    // самые «верхние» пустые клетки — в конце массива order
    static uint16_t empt[WMAX * WMAX];
    int ne = 0;
    for (int k = n - 1; k >= 0; --k) if (!cell[order[k]]) empt[ne++] = order[k];
    if (!ne) return;
    int top = ne / 2 + 1; if (top > ne) top = ne;
    for (int k = 0; k < n; ++k) {
      int idx = order[k];
      if (!cell[idx] || !rng.chance(pct)) continue;
      int j = rng.range(top);
      int dst = empt[j];
      if (cell[dst]) continue;
      cell[dst] = cell[idx]; cell[idx] = 0;
      empt[j] = idx;                            // освободившуюся клетку можно занять снова
    }
  }

  // lum — яркость клетки, foam — поверхность (над ней пусто против гравитации)
  void render(uint8_t* lum, uint8_t* foam) const {
    int ux = -WDX[sector], uy = -WDY[sector];
    for (int y = 0; y < H; ++y)
      for (int x = 0; x < W; ++x) {
        int i = y * W + x;
        lum[i] = cell[i];
        foam[i] = cell[i] && freeAt(x + ux, y + uy);
      }
  }
};

// ---------------- Режим 1: водопад-струи ----------------
struct Rain {
  struct Drop { float p, v; uint8_t lane, len, on; };
  Drop d[64];
  uint8_t pool[WMAX];          // всплеск внизу
  int W = 16, H = 16;
  XorRng rng;

  void init(int w, int h) {
    W = w > WMAX ? WMAX : w; H = h > WMAX ? WMAX : h;
    memset(d, 0, sizeof(d)); memset(pool, 0, sizeof(pool));
  }

  // dir: 0=вниз, 1=влево, 2=вверх, 3=вправо; density 0..100; speed 1..10
  void step(int dir, int density, int speed, uint8_t* lum, uint8_t* foam) {
    int L = (dir & 1) ? W : H;       // длина падения
    int lanes = (dir & 1) ? H : W;
    float base = 0.25f + speed * 0.06f;
    for (auto& q : d) {
      if (!q.on) continue;
      q.p += q.v; q.v += 0.02f;
      if (q.p - q.len > L) q.on = 0;
      else if ((int)q.p == L) { if (pool[q.lane] < 200) pool[q.lane] += 120; }
    }
    int want = lanes * density / 100 / 2 + 1;
    int active = 0; for (auto& q : d) active += q.on;
    if (active < want && rng.chance(60)) {
      for (auto& q : d) if (!q.on) {
        q.on = 1; q.lane = rng.range(lanes); q.p = 0; q.v = base + rng.range(20) / 100.0f;
        q.len = 3 + rng.range(4);
        break;
      }
    }
    memset(lum, 0, W * H); memset(foam, 0, W * H);
    for (auto& q : d) {
      if (!q.on) continue;
      for (int k = 0; k < q.len; ++k) {
        int pos = (int)q.p - k;
        if (pos < 0 || pos >= L) continue;
        int x, y;
        mapLane(dir, q.lane, pos, x, y);
        int v = 255 - k * 200 / q.len;
        int i = y * W + x;
        if (v > lum[i]) lum[i] = v;
        if (k == 0) foam[i] = 1;
      }
    }
    for (int l = 0; l < lanes; ++l) {
      int x, y; mapLane(dir, l, L - 1, x, y);
      int i = y * W + x;
      int v = 50 + pool[l];
      if (v > 255) v = 255;
      if (v > lum[i]) lum[i] = v;
      if (pool[l] > 60) foam[i] = 1;
      pool[l] = pool[l] * 7 / 10;
    }
  }

  void mapLane(int dir, int lane, int pos, int& x, int& y) const {
    switch (dir & 3) {
      case 0: x = lane; y = pos; break;
      case 1: x = W - 1 - pos; y = lane; break;
      case 2: x = lane; y = H - 1 - pos; break;
      default: x = pos; y = lane; break;
    }
  }
};
