@echo off
echo Building SnapActions...
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o bin\publish
if %ERRORLEVEL% EQU 0 (
    echo.
    echo Build successful! Output: bin\publish\SnapActions.exe
) else (
    echo.
    echo Build failed!
)
pause
