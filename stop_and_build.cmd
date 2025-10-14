@echo off
setlocal
cd /d %~dp0

echo Attempting to stop Asp.Net.Test...
powershell -NoLogo -NoProfile -Command ^
  "$procs = Get-CimInstance Win32_Process -Filter \"Name='dotnet.exe'\" | Where-Object { $_.CommandLine -match 'Asp.Net.Test.dll' }; if ($procs) { foreach ($p in $procs) { try { Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop; Write-Host \"Stopped dotnet PID $($p.ProcessId).\" } catch {} } } else { Write-Host 'No Asp.Net.Test process found.' }"

echo Building CacheManager.sln...
dotnet build CacheManager.sln
exit /b %errorlevel%
