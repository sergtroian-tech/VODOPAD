#include <pty.h>
#include <termios.h>
int pinModes[64]; int showCount=0, litShows=0; int LED_REAL_PIN=4;
int digitalRead(int p){ if(pinModes[p]==9 /*pulldown*/) return (p==5||p==6); return 1; }
#include "Arduino.h"
#include "Wire.h"
EspClass ESP; Stream Serial; TwoWire Wire;
#include "/home/claude/fw/Vodopad/Vodopad.ino"
int main(){
  int m,s; char name[64];
  openpty(&m,&s,name,NULL,NULL);
  termios t; tcgetattr(s,&t); cfmakeraw(&t); tcsetattr(s,TCSANOW,&t);
  fcntl(m,F_SETFL,O_NONBLOCK);
  Serial.fd=m;
  unlink("/dev/ttyUSB0"); symlink(name,"/dev/ttyUSB0");
  fprintf(stderr,"[панель] порт %s -> /dev/ttyUSB0\n",name);
  setup();
  for(;;) loop();
}
