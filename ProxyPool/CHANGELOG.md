# Changelog

All notable changes to ProxyPool will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Planning
- Unit test suite
- Integration tests
- Performance benchmarks
- CI/CD pipeline
- Docker support
- Additional proxy authentication methods

## [1.0.0] - 2025-12-17

### Initial Release

#### Added
- `ProxyEnabledHttpClient` - Main HTTP client with automatic proxy management
- `ProxyManager` - Simplified API for common use cases
- Automatic proxy discovery from multiple URL sources
- Parallel proxy testing with configurable concurrency (1-500 parallel tests)
- Intelligent health checking and reliability scoring (0.0-1.0 scale)
- Smart proxy rotation based on performance metrics
- Support for HTTP, HTTPS, SOCKS4, and SOCKS5 proxies
- Authentication support (username/password)
- Configurable timeouts for testing and fetching
- Retry logic with configurable attempts (default: 3)
- Optional direct connection fallback
- Custom user agent support
- Thread-safe concurrent proxy pool management
- Response time tracking (rolling average of last 10 requests)
- Periodic health checks (every 30 minutes by default)
- Comprehensive statistics API
- XML documentation for all public APIs

#### Features
- **Proxy Discovery**: Fetches and parses proxy lists from HTTP/HTTPS URLs
- **Proxy Testing**: Tests proxies against configurable test URLs
- **Health Monitoring**: Tracks success/failure counts and reliability scores
- **Smart Selection**: Prioritizes healthy proxies with high reliability
- **Performance Tracking**: Monitors average response times per proxy
- **Automatic Cleanup**: Removes consistently failing proxies (3+ consecutive failures)
- **Cancellation Support**: Full CancellationToken support for all async operations
- **Resource Management**: Proper IDisposable implementation

#### Configuration Options
- `proxyListUrls` - List of URLs to fetch proxies from
- `testTimeoutSeconds` - Timeout for individual proxy tests (min: 1s)
- `fetchTimeoutSeconds` - Timeout for content fetching (min: 5s)
- `maxParallelTests` - Maximum concurrent proxy tests (range: 1-500)
- `maxRetries` - Maximum retry attempts per proxy (min: 1, default: 3)
- `allowDirectFallback` - Enable direct connection fallback (default: false)
- `userAgent` - Custom user agent string (default: Chrome UA)
- `healthCheckIntervalMinutes` - Health check frequency (min: 5, default: 30)

#### Public API
- `FetchHtmlAsync(string url, CancellationToken cancellationToken)` - Fetch content through proxies
- `GetStatistics()` - Get current proxy pool statistics
- `Dispose()` - Clean up resources

#### Statistics
- `TotalProxies` - Total proxies in pool
- `HealthyProxies` - Count of healthy proxies
- `IsInitialized` - Initialization status
- `TopProxies` - Top 10 proxies by reliability

#### Documentation
- Comprehensive README with quick start guide
- Configuration reference
- API documentation
- Advanced usage examples
- Best practices and troubleshooting
- Contributing guidelines
- Code examples for common scenarios

#### Examples
- `BasicUsage.cs` - Fundamental usage patterns
- `ProxyManagerExample.cs` - Simplified API usage
- `AdvancedUsage.cs` - Advanced scenarios and monitoring
- Configuration patterns for different use cases

#### Internal Architecture
- `ProxyInfo` class - Proxy metadata and health information
- `ProxyType` enum - Http, Https, Socks4, Socks5
- `ProxyPoolStatistics` - Aggregated pool statistics
- `ProxyStatistics` - Individual proxy statistics
- Concurrent dictionary for thread-safe proxy storage
- Semaphore-based concurrency control
- Timer-based periodic health checks

#### Quality
- Clean, well-documented codebase
- XML documentation on all public members
- Proper exception handling
- Thread-safe implementation
- Resource cleanup via IDisposable
- No commented-out code
- Professional code structure

### Dependencies
- .NET 10.0
- System.Net.Http
- System.Threading.Tasks
- No external NuGet packages required

### License
- Apache License 2.0

---

## Version History

- **1.0.0** (2025-12-17) - Initial open source release

## Upgrade Guide

### Upgrading to 1.0.0
This is the initial release. No upgrade path required.

## Breaking Changes

### 1.0.0
None - Initial release

## Deprecations

### 1.0.0
None - Initial release

## Security

### 1.0.0
- Proper timeout handling prevents indefinite hangs
- No credential logging or exposure
- Secure proxy authentication support
- Exception handling prevents information leakage

## Contributors

Thank you to all contributors who helped make this release possible!

---

[Unreleased]: https://github.com/yourusername/ProxyPool/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/yourusername/ProxyPool/releases/tag/v1.0.0
