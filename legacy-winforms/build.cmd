@echo off
rem grobyq build (Windows, .NET SDK 8+). Usage:  build.cmd [license-list-raw-url]
setlocal
set "HERE=%~dp0"
set "SRC=%HERE%src"
set "URL=%~1"
if "%URL%"=="" set "URL=https://raw.githubusercontent.com/mitzuLLC/grobpc-licenses/main/licenses.json"
set /p PUB=<"%HERE%grob_pub_spki.b64"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t = Get-Content -Raw -LiteralPath '%SRC%\Licensing.cs.tmpl'; $t = $t.Replace('@@GROB_PUBKEY@@','%PUB%').Replace('@@LIST_URL@@','%URL%'); [IO.File]::WriteAllText('%SRC%\Licensing.cs', $t)"
if errorlevel 1 exit /b 1
dotnet build "%SRC%\grobyq.csproj" -c Release
if errorlevel 1 exit /b 1
echo.
echo Output: %SRC%\bin\Release\net48\grobyq.exe
echo List URL baked in: %URL%
