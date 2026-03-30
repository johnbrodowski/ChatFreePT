# Contributing to ProxyPool

Thank you for your interest in contributing to ProxyPool! This document provides guidelines and instructions for contributing to the project.

## Code of Conduct

By participating in this project, you agree to maintain a respectful and inclusive environment for all contributors.

## How to Contribute

### Reporting Issues

If you find a bug or have a feature request:

1. **Search existing issues** to avoid duplicates
2. **Create a new issue** with a clear title and description
3. **Include relevant information**:
   - ProxyPool version
   - .NET version
   - Operating system
   - Steps to reproduce (for bugs)
   - Expected vs actual behavior
   - Code samples or error messages

### Submitting Changes

1. **Fork the repository** on GitHub
2. **Create a feature branch** from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   ```
3. **Make your changes** following our coding standards
4. **Test your changes** thoroughly
5. **Commit your changes** with clear, descriptive messages:
   ```bash
   git commit -m "Add feature: brief description"
   ```
6. **Push to your fork**:
   ```bash
   git push origin feature/your-feature-name
   ```
7. **Submit a pull request** to the main repository

## Coding Standards

### C# Style Guidelines

- Follow standard C# naming conventions:
  - PascalCase for public members and types
  - camelCase for private fields (with underscore prefix: `_fieldName`)
  - PascalCase for methods and properties
- Use meaningful, descriptive names for variables and methods
- Keep methods focused and concise (Single Responsibility Principle)
- Use XML documentation comments for public APIs:
  ```csharp
  /// <summary>
  /// Fetches HTML content from the specified URL
  /// </summary>
  /// <param name="url">The URL to fetch</param>
  /// <returns>HTML content as a string</returns>
  public async Task<string> FetchHtmlAsync(string url)
  ```

### Code Quality

- **No commented-out code**: Remove it or explain why it's there
- **Minimal comments**: Write self-documenting code; use comments only for complex logic
- **Error handling**: Handle exceptions appropriately and log meaningful messages
- **Thread safety**: Use appropriate synchronization for shared state
- **Resource management**: Properly dispose of resources using `using` statements or `IDisposable`
- **Async/await**: Use async patterns consistently for I/O operations

### Testing

- Add unit tests for new functionality
- Ensure all existing tests pass
- Test edge cases and error conditions
- Include integration tests for complex features

## Pull Request Guidelines

### Before Submitting

- [ ] Code follows the project's style guidelines
- [ ] All tests pass locally
- [ ] New code includes appropriate tests
- [ ] Documentation is updated (README, XML comments)
- [ ] Commit messages are clear and descriptive
- [ ] No unnecessary files are included (build artifacts, IDE files)

### PR Description Template

```markdown
## Description
Brief description of the changes

## Type of Change
- [ ] Bug fix (non-breaking change that fixes an issue)
- [ ] New feature (non-breaking change that adds functionality)
- [ ] Breaking change (fix or feature that causes existing functionality to change)
- [ ] Documentation update

## Testing
Describe the tests you ran and how to reproduce them

## Checklist
- [ ] My code follows the project's style guidelines
- [ ] I have performed a self-review of my code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have updated the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix/feature works
- [ ] New and existing tests pass locally
```

## Development Setup

### Prerequisites

- .NET 10.0 SDK or later
- Git
- Your favorite IDE (Visual Studio, VS Code, Rider)

### Building the Project

```bash
# Clone your fork
git clone https://github.com/YOUR_USERNAME/ProxyPool.git
cd ProxyPool

# Build the project
dotnet build

# Run tests (when available)
dotnet test
```

### Project Structure

```
ProxyPool/
├── ProxyPool/                  # Main library project
│   ├── ProxyEnabledHttpClient.cs
│   └── ProxyPool.csproj
├── ProxyPool.sln              # Solution file
├── README.md                  # Project documentation
├── CONTRIBUTING.md            # This file
├── LICENSE.txt               # Apache 2.0 license
└── CHANGELOG.md              # Version history
```

## Areas for Contribution

We welcome contributions in these areas:

### Features

- Additional proxy types support
- Proxy authentication methods
- Custom health check strategies
- Performance optimizations
- Configurable retry strategies
- Proxy blacklist/whitelist
- Custom parsing formats

### Documentation

- Usage examples
- Tutorial articles
- API documentation improvements
- Troubleshooting guides
- Video tutorials

### Testing

- Unit tests
- Integration tests
- Performance benchmarks
- Edge case testing

### Infrastructure

- CI/CD pipeline improvements
- NuGet packaging
- Docker support
- Sample applications

## Commit Message Guidelines

Write clear, concise commit messages that explain the "what" and "why":

### Format

```
<type>: <subject>

<body>

<footer>
```

### Types

- **feat**: New feature
- **fix**: Bug fix
- **docs**: Documentation changes
- **style**: Code style changes (formatting, no logic change)
- **refactor**: Code refactoring
- **perf**: Performance improvements
- **test**: Adding or updating tests
- **chore**: Build process or tooling changes

### Examples

```
feat: Add support for SOCKS4a proxies

Implements parsing and connection handling for SOCKS4a protocol.
Includes tests for authentication and hostname resolution.

Closes #123
```

```
fix: Handle null response in FetchHtmlAsync

Previously, null responses from failed proxy connections would
throw NullReferenceException. Now returns empty string as documented.

Fixes #456
```

## Review Process

1. A maintainer will review your PR within a few days
2. They may request changes or ask questions
3. Address feedback and push updates to your branch
4. Once approved, a maintainer will merge your PR

### What We Look For

- Code quality and maintainability
- Test coverage
- Documentation completeness
- Backward compatibility
- Performance impact
- Security considerations

## Release Process

Releases follow semantic versioning (MAJOR.MINOR.PATCH):

- **MAJOR**: Breaking changes
- **MINOR**: New features (backward compatible)
- **PATCH**: Bug fixes (backward compatible)

## Getting Help

- **Questions**: Open a GitHub Discussion
- **Bugs**: Create an issue with the "bug" label
- **Features**: Create an issue with the "enhancement" label

## License

By contributing to ProxyPool, you agree that your contributions will be licensed under the Apache License 2.0.

## Recognition

Contributors will be recognized in:
- The project's README
- Release notes
- GitHub contributors page

Thank you for helping make ProxyPool better!
