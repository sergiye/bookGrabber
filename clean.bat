@echo off

rmdir /s /q .vs
rmdir /s /q .idea

rmdir /s /q .\bin
rmdir /s /q .\obj

del /S ".\FodyWeavers.xsd"
