#include "/home/claude/fw/Vodopad/water.h"
#include <cstdio>
Water w; Rain r; uint8_t lum[1024], foam[1024];
void show(const char* t){ printf("%s\n",t); for(int y=0;y<w.H;y++){ for(int x=0;x<w.W;x++) putchar(w.cell[y*w.W+x]?'#':'.'); putchar('\n');} }
int main(){
  w.init(16,16,55,3); int n0=w.count(); printf("count=%d\n",n0);
  for(int i=0;i<60;i++) w.step(-1,0.3f);  show("наклон влево (как на фото):");
  for(int i=0;i<60;i++) w.step(0.7f,0.7f); show("наклон вправо-вниз:");
  w.splash(0.7f,0.7f,30); show("брызги:");
  for(int i=0;i<40;i++) w.step(0,-1); show("вверх ногами:");
  printf("count after=%d (must be %d)\n", w.count(), n0);
  int bad=0; for(int k=0;k<2000;k++){ float a=k*0.05f; w.step(cosf(a),sinf(a)); if(k%97==0) w.splash(cosf(a),sinf(a),40);} 
  printf("after 2000 random steps count=%d\n", w.count());
  r.init(16,16); for(int i=0;i<40;i++) r.step(0,55,6,lum,foam);
  for(int y=0;y<16;y++){for(int x=0;x<16;x++) putchar(lum[y*16+x]>150?'#':lum[y*16+x]?'+':'.'); putchar('\n');}
}
