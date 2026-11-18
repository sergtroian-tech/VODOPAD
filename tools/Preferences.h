#pragma once
#include "Arduino.h"
class Preferences { public:
  void begin(const char*, bool){}
  size_t getBytesLength(const char*){ FILE* f=fopen("/tmp/host/nvs.bin","rb"); if(!f) return 0; fseek(f,0,SEEK_END); long n=ftell(f); fclose(f); return n; }
  size_t getBytes(const char*, void* b, size_t n){ FILE* f=fopen("/tmp/host/nvs.bin","rb"); if(!f) return 0; size_t r=fread(b,1,n,f); fclose(f); return r; }
  size_t putBytes(const char*, const void* b, size_t n){ FILE* f=fopen("/tmp/host/nvs.bin","wb"); fwrite(b,1,n,f); fclose(f); fprintf(stderr,"[панель] настройки сохранены\n"); return n; }
};
