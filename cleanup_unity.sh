#!/bin/bash

# Upewnij się, że jesteś w folderze projektu
echo "Czyszczenie projektu Unity..."

# Usuwanie folderów generowanych przez Unity
rm -rf Library
rm -rf Temp
rm -rf Obj
rm -rf Logs
rm -rf UserSettings
rm -rf .vs

# Usuwanie plików wygenerowanych przez IDE
find . -name "*.sln" -type f -delete
find . -name "*.csproj" -type f -delete
find . -name "*.user" -type f -delete
find . -name "packages-lock.json" -type f -delete

echo "Czyszczenie zakończone. Możesz teraz otworzyć Unity."
