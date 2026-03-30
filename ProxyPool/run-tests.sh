#!/bin/bash

# ProxyPool Test Runner
# This script runs the ProxyPool tests

echo "=========================================="
echo "ProxyPool Test Runner"
echo "=========================================="
echo ""

# Check if dotnet is available
if ! command -v dotnet &> /dev/null; then
    echo "ERROR: dotnet CLI not found!"
    echo "Please install .NET 10 SDK from: https://dotnet.microsoft.com/download"
    exit 1
fi

echo "✓ .NET SDK found: $(dotnet --version)"
echo ""

# Build the solution
echo "Building ProxyPool solution..."
dotnet build ProxyPool.sln --configuration Release

if [ $? -ne 0 ]; then
    echo ""
    echo "✗ Build failed!"
    exit 1
fi

echo ""
echo "✓ Build successful!"
echo ""
echo "=========================================="
echo "Running Tests..."
echo "=========================================="
echo ""

# Run the tests with xUnit
dotnet test ProxyPool.Tests/ProxyPool.Tests.csproj --configuration Release --logger "console;verbosity=normal"

TEST_RESULT=$?

echo ""
echo "=========================================="
if [ $TEST_RESULT -eq 0 ]; then
    echo "✓ Tests completed successfully!"
else
    echo "✗ Tests failed with exit code: $TEST_RESULT"
fi
echo "=========================================="

exit $TEST_RESULT
