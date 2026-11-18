#!/bin/bash
cd /home/claude/exe
R=""; for c in esp32 esp32c3 esp32s3 esp32s2; do for f in boot part app; do R="$R -resource:/home/claude/fw/bins/$c/$f.bin,res/fw/$c/$f.bin"; done; done
R="$R -resource:/home/claude/fw/bins/esp8266/app.bin,res/fw/esp8266/app.bin -resource:esptool.exe,res/esptool.exe"
mcs -sdk:4.5 -target:winexe -optimize+ -codepage:utf8 -nowarn:414 -out:Vodopad.exe -win32icon:vodopad.ico -r:System.Windows.Forms.dll -r:System.Drawing.dll -r:System.Management.dll -r:System.dll -r:System.Core.dll $R Program.cs Core.cs MainForm.cs Updater.cs
