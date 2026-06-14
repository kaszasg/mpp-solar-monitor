@echo off
rem dotnet publish Solar.csproj -r linux-x64 -p:PublishSingleFile=true -p:PublishTrimmed=true --self-contained true -c Release -o publish
dotnet publish Solar.csproj -r linux-x64 -p:PublishSingleFile=true --self-contained true -c Release -o publish