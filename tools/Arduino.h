#pragma once
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <math.h>
#include <ctype.h>
#include <unistd.h>
#include <fcntl.h>
#include <time.h>
#include <algorithm>
typedef bool boolean;
#define CONFIG_IDF_TARGET_ESP32C3 1
#define ARDUINO_USB_CDC_ON_BOOT 0
#define IRAM_ATTR
#define INPUT 1
#define OUTPUT 2
#define INPUT_PULLUP 5
#define INPUT_PULLDOWN 9
#define HIGH 1
#define LOW 0
#define CHANGE 3
#define NOT_AN_INTERRUPT -1
inline uint32_t millis(){ timespec t; clock_gettime(CLOCK_MONOTONIC,&t); return t.tv_sec*1000+t.tv_nsec/1000000; }
inline void delay(uint32_t ms){ usleep(ms*1000); }
inline void delayMicroseconds(uint32_t us){ usleep(us); }
inline void yield(){}
extern int pinModes[64];
extern int i2cPins[2];
inline void pinMode(int p,int m){ pinModes[p]=m; }
int digitalRead(int p);
inline void digitalWrite(int,int){}
inline int digitalPinToInterrupt(int p){ return p; }
inline void attachInterrupt(int, void(*)(), int){}
inline void detachInterrupt(int){}
inline bool psramFound(){ return false; }
#define constrain(x,a,b) ((x)<(a)?(a):((x)>(b)?(b):(x)))
struct EspClass { void restart(){ exit(0);} }; extern EspClass ESP;
class Stream {
public:
  int fd=-1;
  int available(){ char c; int r=::read(fd,&c,1); if(r==1){ pend=c; has=1; return 1;} return has; }
  int read(){ if(has){has=0; return (unsigned char)pend;} char c; return ::read(fd,&c,1)==1?(unsigned char)c:-1; }
  size_t printf(const char* f, ...){ char b[512]; va_list a; va_start(a,f); int n=vsnprintf(b,sizeof b,f,a); va_end(a); ::write(fd,b,n); return n; }
  size_t print(const char* s){ return ::write(fd,s,strlen(s)); }
  size_t print(char c){ return ::write(fd,&c,1); }
  size_t print(int v){ return printf("%d",v); }
  void flush(){}
  void begin(int){}
private: char pend; int has=0;
};
extern Stream Serial;
