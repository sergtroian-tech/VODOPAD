#!/bin/bash
cd /home/claude/fw && export PATH=$PATH:/home/claude/dl
rm -rf out bins; mkdir -p bins logs
build(){ name=$1; fqbn=$2; shift 2
 timeout 900 arduino-cli compile --fqbn "$fqbn" --build-property "runtime.tools.ctags.path=/root/tc/fakectags" "$@" --output-dir out/$name Vodopad > logs/$name.log 2>&1
 rc=$?; echo "$name rc=$rc $(grep -E 'Sketch uses' logs/$name.log | cut -c1-40)"
 [ $rc = 0 ] || grep -vE "Error initializing" logs/$name.log | grep -iE "error" | head -5
 mkdir -p bins/$name; cp out/$name/Vodopad.ino.bin bins/$name/app.bin
 [ -f out/$name/Vodopad.ino.bootloader.bin ] && cp out/$name/Vodopad.ino.bootloader.bin bins/$name/boot.bin
 [ -f out/$name/Vodopad.ino.partitions.bin ] && cp out/$name/Vodopad.ino.partitions.bin bins/$name/part.bin
 true; }
build esp32c3 espressif:esp32:esp32c3:CDCOnBoot=cdc,FlashMode=dio
build esp32s3 espressif:esp32:esp32s3:CDCOnBoot=cdc,FlashMode=dio
build esp32s2 espressif:esp32:esp32s2:CDCOnBoot=cdc,FlashMode=dio
build esp32   espressif:esp32:esp32:FlashMode=dio
build esp8266 esp8266com:esp8266:generic --build-property "runtime.tools.python3.path=/usr/local/bin" --build-property "runtime.tools.xtensa-lx106-elf-gcc.path=/root/tc/xtensa-lx106-elf"
ls -la bins/*
