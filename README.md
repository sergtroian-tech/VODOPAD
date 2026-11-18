# Водопад — прошивальщик LED-панели

Программа для Windows (`Vodopad.exe`), которая перепрошивает LED-матрицу с датчиком движения
(ESP32 / ESP32-C3 / ESP32-S3 / ESP32-S2 / ESP8266) на «только водопад»: вода переливается по
наклону, при встряхивании — брызги. Сохраняет и возвращает родную прошивку.

**Скачать:** [последняя версия](https://github.com/sergtroian-tech/vodopad/releases/latest/download/Vodopad.exe)

Программа обновляется сама: при запуске читает `version.txt` из последнего релиза и,
если версия новее, скачивает `Vodopad.exe`, подменяет себя и перезапускается.

## Как выпустить обновление
1. Поднять `AssemblyVersion` в `app/Program.cs` (например 1.2.0.0).
2. Если менялась прошивка — поднять `FW_VERSION` в `firmware/Vodopad/Vodopad.ino`
   и `PanelFwVersion` в `app/MainForm.cs`, затем `firmware/build_all.sh`.
3. Собрать `app/build.sh` (mono `mcs`; встраивает esptool.exe v4.8.1 win64 и прошивки).
4. Создать релиз `v1.2` с двумя файлами: `Vodopad.exe` и `version.txt` (внутри `1.2`).

## Устройство
- `firmware/Vodopad` — прошивка (Arduino-ESP32 2.0.17 / ESP8266 3.1.2, Adafruit NeoPixel).
  Текстовые команды по USB 115200: `PING, GET, SET, SAVE, LEDPINS, TRYLED, SCANI2C, USEI2C,
  SCANSW, USESW, ORIENT, RGBTEST, CAL UP|FLAT, RUN, SPLASH, DEFAULTS`.
- `app` — WinForms (.NET Framework 4), esptool для чтения/записи флеша.
- `tools` — проверка прошивки на ПК с имитацией платы и тест симуляции воды.
