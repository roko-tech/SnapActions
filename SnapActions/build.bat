@echo off
echo Building and verifying a fresh SnapActions package...
python "%~dp0..\tools\package.py"
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Package verified. See the artifacts directory for the ZIP and checksums.
) else (
    echo.
    echo Build failed!
)
pause
