@echo off
REM ProxyPool Test Runner (Windows)
REM This script runs the ProxyPool tests

echo ==========================================
echo ProxyPool Test Runner
echo ==========================================
echo.

REM Check if dotnet is available
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo ERROR: dotnet CLI not found!
    echo Please install .NET 10 SDK from: https://dotnet.microsoft.com/download
    exit /b 1
)

echo OK: .NET SDK found
dotnet --version
echo.

REM Build the solution
echo Building ProxyPool solution...
dotnet build ProxyPool.sln --configuration Release

if %ERRORLEVEL% neq 0 (
    echo.
    echo BUILD FAILED!
    exit /b 1
)

echo.
echo OK: Build successful!
echo.
echo ==========================================
echo Running Tests...
echo ==========================================
echo.

REM Run the tests with xUnit
dotnet test ProxyPool.Tests\ProxyPool.Tests.csproj --configuration Release --logger "console;verbosity=normal"

set TEST_RESULT=%ERRORLEVEL%

echo.
echo ==========================================
if %TEST_RESULT% equ 0 (
    echo OK: Tests completed successfully!
) else (
    echo FAILED: Tests failed with exit code: %TEST_RESULT%
)
echo ==========================================

exit /b %TEST_RESULT%
