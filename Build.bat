@echo off
echo ================================================
echo MultiBoardViewer - Build Script (.NET)
echo ================================================
echo.

REM Kiểm tra .NET SDK
dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo ERROR: Khong tim thay .NET SDK!
    echo Vui long cai dat .NET SDK
    echo Download: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo Da tim thay .NET SDK
dotnet --version
echo.
echo Dang build project...
echo.

dotnet build MultiBoardViewer.sln -c Release

if %ERRORLEVEL% EQU 0 (
    echo.
    echo ================================================
    echo BUILD THANH CONG!
    echo ================================================
    echo.
    echo File exe: MultiBoardViewer\bin\Release\net8.0-windows\MultiBoardViewer.exe
    echo.
    
    REM Copy BoardViewer folder
    echo Dang copy BoardViewer...
    if exist "BoardViewer" (
        xcopy /E /I /Y "BoardViewer" "MultiBoardViewer\bin\Release\net8.0-windows\BoardViewer"
        echo Da copy BoardViewer thanh cong!
    ) else (
        echo WARNING: Khong tim thay folder BoardViewer!
    )
    echo.
    
    REM Copy SumatraPDF folder
    echo Dang copy SumatraPDF...
    if exist "SumatraPDF" (
        xcopy /E /I /Y "SumatraPDF" "MultiBoardViewer\bin\Release\net8.0-windows\SumatraPDF"
        echo Da copy SumatraPDF thanh cong!
    ) else (
        echo WARNING: Khong tim thay folder SumatraPDF!
    )
    echo.
    
    REM Mở thư mục output
    if exist "MultiBoardViewer\bin\Release\net8.0-windows\MultiBoardViewer.exe" (
        echo Mo thu muc output...
        explorer "MultiBoardViewer\bin\Release\net8.0-windows"
    )
) else (
    echo.
    echo ================================================
    echo BUILD THAT BAI!
    echo ================================================
    echo Vui long kiem tra loi o tren
)

echo.
pause
