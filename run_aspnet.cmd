@echo off
setlocal
cd /d %~dp0

echo Starting Asp.Net.Test...
dotnet run --project Asp.Net.Test\Asp.Net.Test.csproj
