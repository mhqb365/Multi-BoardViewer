@echo off
echo ================================================
echo MultiBoardViewer - Quick Start
echo ================================================
echo.

REM Kiểm tra xem đã build chưa
if not exist "MultiBoardViewer\bin\Release\net8.0-windows\MultiBoardViewer.exe" (
    if not exist "MultiBoardViewer\bin\Debug\net8.0-windows\MultiBoardViewer.exe" (
        echo Chua build project!
        echo Vui long chay Build.bat truoc
        echo.
        pause
        exit /b 1
    )
    echo Chay phien ban Debug...
    start "" "MultiBoardViewer\bin\Debug\net8.0-windows\MultiBoardViewer.exe"
) else (
    echo Chay phien ban Release...
    start "" "MultiBoardViewer\bin\Release\net8.0-windows\MultiBoardViewer.exe"
)

echo.
echo Da khoi dong MultiBoardViewer!
echo.
