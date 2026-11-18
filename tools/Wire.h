#pragma once
#include "Arduino.h"
// Имитация: MPU6050 на SDA=5, SCL=6, адрес 0x68, лежит под наклоном
class TwoWire { public:
  int sda=-1,scl=-1; uint8_t addr, reg; bool on=false; uint8_t rx[16]; int rxn=0, rxi=0; int txn=0; uint8_t tx[8];
  bool begin(int s,int c,uint32_t){ sda=s; scl=c; on=true; return true; }
  void end(){ on=false; }
  void setTimeOut(int){}
  void beginTransmission(uint8_t a){ addr=a; txn=0; }
  size_t write(uint8_t v){ if(txn<8) tx[txn++]=v; return 1; }
  bool ok(){ return on && sda==5 && scl==6 && addr==0x68; }
  uint8_t endTransmission(bool=true){ if(!ok()) return 2; if(txn>=1) reg=tx[0]; return 0; }
  uint8_t requestFrom(uint8_t a, uint8_t n){ addr=a; if(!ok()) return 0; rxn=n; rxi=0;
    for(int i=0;i<n;i++){ uint8_t r=reg+i; uint8_t v=0;
      if(r==0x75) v=0x68;
      // ax=+0.5g, ay=-0.8g, az=+0.3g  (16384/g, big-endian)
      int16_t ax=8192, ay=-13107, az=4915;
      if(r==0x3B) v=ax>>8; if(r==0x3C) v=ax&255; if(r==0x3D) v=ay>>8; if(r==0x3E) v=ay&255; if(r==0x3F) v=az>>8; if(r==0x40) v=az&255;
      rx[i]=v; }
    return n; }
  int read(){ return rxi<rxn? rx[rxi++] : -1; }
};
extern TwoWire Wire;
