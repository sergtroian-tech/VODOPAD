#pragma once
#include "Arduino.h"
typedef uint16_t neoPixelType;
#define NEO_GRB 0x52
#define NEO_RGB 0x06
#define NEO_BRG 0xA1
#define NEO_RBG 0x09
#define NEO_GBR 0x92
#define NEO_BGR 0xA4
#define NEO_KHZ800 0
extern int showCount, litShows; extern int LED_REAL_PIN;
class Adafruit_NeoPixel { public:
  int n, pin; uint32_t px[1024]; uint8_t bri=255;
  Adafruit_NeoPixel(int nn,int p,int){ n=nn; pin=p; clear(); }
  void begin(){} void setPin(int p){ pin=p; } void updateType(int){} void updateLength(int nn){ n=nn; }
  void setBrightness(uint8_t b){ bri=b; } void clear(){ memset(px,0,sizeof px); }
  void setPixelColor(int i,uint32_t c){ if(i>=0&&i<n) px[i]=c; }
  static uint32_t Color(uint8_t r,uint8_t g,uint8_t b){ return (r<<16)|(g<<8)|b; }
  static uint32_t ColorHSV(uint16_t,uint8_t,uint8_t v){ return v; }
  static uint32_t gamma32(uint32_t c){ return c; }
  void show(){ showCount++; int lit=0; for(int i=0;i<n;i++) if(px[i]) lit++; if(pin==LED_REAL_PIN && lit) { litShows++; fprintf(stderr,"[панель] ГОРИТ %d светодиодов (GPIO%d)\n",lit,pin);} }
};
