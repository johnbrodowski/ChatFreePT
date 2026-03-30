# ProxyPool Tests

This is an xUnit test project to verify that ProxyPool is working correctly and able to find and test proxies.

## What This Tests

The test suite validates:

1. **ProxyEnabledHttpClient** - Core functionality
   - `ProxyClient_ShouldDiscoverProxies` - Proxy discovery from remote sources
   - `ProxyClient_ShouldTrackStatistics` - Health tracking and statistics
   - `ProxyClient_ShouldHandleMultipleRequests` - Multiple sequential requests

2. **ProxyManager** - Simplified API
   - `ProxyManager_ShouldInitializeSuccessfully` - Proxy pool initialization
   - `ProxyManager_ShouldFetchContent` - Content fetching with managed proxies

## Running the Tests

### From Visual Studio
1. Open **Test Explorer** (Test > Test Explorer)
2. Click **Run All** to run all tests
3. View detailed output for each test

### From Visual Studio Code
1. Install the .NET Test Explorer extension
2. Tests will appear in the Testing sidebar
3. Click the play button to run tests

### From Command Line

**Run all tests:**
```bash
dotnet test
```

**Run from test project directory:**
```bash
cd ProxyPool.Tests
dotnet test
```

**Run with detailed output:**
```bash
dotnet test --logger "console;verbosity=detailed"
```

**Run specific test:**
```bash
dotnet test --filter "ProxyClient_ShouldDiscoverProxies"
```

## Expected Output

The test will:
1. Download a list of proxies from GitHub
2. Test multiple proxies in parallel
3. Find working proxies
4. Fetch content through those proxies
5. Display statistics

### Successful Run Example
```
==========================================
ProxyPool Functionality Test
==========================================

TEST 1: ProxyEnabledHttpClient - Basic Functionality
---------------------------------------------
✓ Using proxy source: https://raw.githubusercontent.com/...
✓ ProxyEnabledHttpClient created

Test 1.1: Fetching content through proxies...
Target URL: https://example.com
✓ SUCCESS: Fetched 1256 characters in 3241ms
  Content preview: <!doctype html>...

Test 1.2: Checking proxy pool statistics...

Proxy Pool Statistics:
  Total Proxies: 127
  Healthy Proxies: 23
  Initialized: True
  ✓ Successfully discovered and tested 127 proxies
  ✓ Found 23 working proxies!

  Top 5 Working Proxies:
  1. 45.67.231.128:1080
     Type: Socks5
     Reliability: 100%
     Success Rate: 100.0% (1/1)
     Avg Response: 856ms
  ...

TEST 2: ProxyManager - Simplified API
---------------------------------------------
...
```

## What If Tests Fail?

### No Proxies Found
If the test shows "No healthy proxies found", this could mean:
- The proxy source is temporarily unavailable
- Network connectivity issues
- All proxies from the source are currently offline (common with free proxy lists)
- Firewall blocking proxy connections

**Solution:** Try using different proxy sources or run the test again later.

### Timeouts
If the test times out:
- Increase the timeout values in the test code
- Check your internet connection
- Try with fewer parallel tests

### Connection Errors
If you see connection errors:
- Verify you have internet connectivity
- Check if your firewall allows outbound proxy connections
- Some networks block SOCKS5 connections

## Customizing the Tests

### Use Different Proxy Sources
Edit `Program.cs` and change the `proxyListUrls`:

```csharp
var proxyListUrls = new List<string>
{
    "https://raw.githubusercontent.com/MuRongPIG/Proxy-Master/main/socks5.txt",
    "https://raw.githubusercontent.com/monosans/proxy-list/main/proxies/socks5.txt"
};
```

### Adjust Timeouts
Modify the timeout values in the test:

```csharp
testTimeoutSeconds: 10,     // Increase for slower proxies
fetchTimeoutSeconds: 30,    // Increase for slower fetches
maxParallelTests: 50,       // Reduce to test fewer proxies at once
```

### Change Target URL
Test with a different website:

```csharp
string html = await proxyClient.FetchHtmlAsync("https://httpbin.org/ip", cts.Token);
```

## Understanding the Output

### Statistics Explained

- **Total Proxies**: Number of proxy addresses fetched from sources
- **Healthy Proxies**: Number of proxies that successfully passed testing
- **Reliability Score**: 0.0-1.0 rating based on success/failure ratio
- **Success Rate**: Percentage of successful requests vs total attempts
- **Avg Response Time**: Average time to complete requests

### Debug Output

The ProxyPool library outputs debug information to `Debug.WriteLine()`. To see this:
- **Visual Studio**: Check the Debug Output window
- **Command Line**: Use a debug listener or check debugger output

## Troubleshooting

### dotnet command not found
Install .NET 10 SDK from https://dotnet.microsoft.com/download

### Build errors
```bash
# Restore dependencies
dotnet restore

# Clean and rebuild
dotnet clean
dotnet build
```

### Port conflicts
If running multiple tests simultaneously, you may encounter port conflicts. Wait for one test to complete before running another.

## Performance Notes

- First run will be slower as it discovers and tests proxies
- Subsequent fetches will be faster using cached working proxies
- Free proxy lists often have low success rates (10-30%)
- Testing 100+ proxies can take 30-60 seconds
- Working proxies may stop working at any time

## Next Steps

After verifying the tests work:
1. Use ProxyPool in your own applications
2. Refer to the [Examples](../Examples) directory for usage patterns
3. Check the main [README](../README.md) for full documentation

## Continuous Integration

To run these tests in CI/CD:

```bash
# Build the solution
dotnet build ProxyPool.sln

# Run the test program
dotnet run --project ProxyPool.Tests

# Check exit code
echo $?  # Should be 0 on success
```

## Contributing

Found issues or want to add more tests? See [CONTRIBUTING.md](../CONTRIBUTING.md) for guidelines.
