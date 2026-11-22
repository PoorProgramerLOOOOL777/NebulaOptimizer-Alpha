@echo off
setlocal
REM La tarea usa el EXE y el perfil elegido en la aplicacion.
REM No mueva esta carpeta despues de programar el horario.
REM Requiere usuario conectado y equipo despierto.
REM El plan Equilibrado es opcional; no cambia drivers ni GPU dedicada.
REM La limpieza no entra en subcarpetas ni toca documentos.
REM El ejecutable incluido es autonomo para Windows x64.
REM Sin EXE, se compila desde el proyecto con .NET 10 SDK.
cd /d "%~dp0"
if exist "NebulaOptimizer.exe" (
  start "" "NebulaOptimizer.exe"
  exit /b 0
)
where dotnet >nul 2>&1 || (echo Se requiere .NET 10 SDK para compilar. Consulta README.md.& pause & exit /b 1)
dotnet run --project "NebulaOptimizer.csproj" --configuration Release
set "RESULT=%ERRORLEVEL%"
if not "%RESULT%"=="0" pause
exit /b %RESULT%
