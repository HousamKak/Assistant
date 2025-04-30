@echo off
echo Starting Voice Assistant in console mode...
cd /d %~dp0
VoiceAssistant.Service.exe --console
pause