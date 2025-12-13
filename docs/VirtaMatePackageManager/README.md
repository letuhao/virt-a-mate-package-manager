# VirtaMatePackageManager - Technical Documentation

## Overview

**VirtaMatePackageManager** is a modern, high-performance package management system for Virt-a-Mate (VaM). It replaces the legacy varManager system with a complete architectural overhaul, providing scalable VAR (VaM Addon Resource) file management across multiple storage locations and installation targets.

## Version Information

- **Project Name**: VirtaMatePackageManager
- **Version**: 1.0.0 (Initial Release)
- **Target Framework**: .NET 9.0
- **Database**: PostgreSQL
- **Status**: Design Phase

## Project Goals

### Primary Objectives

1. **Performance**: Achieve sub-10-second startup times even with 10,000+ VAR files
2. **Scalability**: Support multiple repositories across different drives and network locations
3. **Reliability**: Modern architecture with proper error handling and transaction support
4. **Maintainability**: Clean architecture with separation of concerns and testability
5. **User Experience**: Responsive UI with real-time progress and cancellation support

### Key Improvements over Legacy System

- ✅ Modern .NET 9.0 with async/await throughout
- ✅ PostgreSQL database replacing Microsoft Access
- ✅ Multiple repository support (cross-drive capability)
- ✅ Multiple installation targets with profile management
- ✅ Virtual scrolling for large datasets
- ✅ Proper dependency injection and testability
- ✅ Comprehensive error handling and recovery

## Documentation Structure

This technical documentation is organized as follows:

1. **[Architecture Overview](./01-Architecture-Overview.md)** - System architecture, design patterns, and technology stack
2. **[Database Schema](./02-Database-Schema.md)** - Complete PostgreSQL database design
3. **[Feature Specifications](./03-Feature-Specifications.md)** - Detailed feature requirements and user stories
4. **[API Specifications](./04-API-Specifications.md)** - Service interfaces and contracts
5. **[UI/UX Design](./05-UI-Design.md)** - User interface design and workflows
6. **[Migration Plan](./06-Migration-Plan.md)** - Migration strategy from legacy varManager
7. **[Development Guide](./07-Development-Guide.md)** - Setup, coding standards, and best practices
8. **[Testing Strategy](./08-Testing-Strategy.md)** - Testing approach and test plans
9. **[Function Specifications](./09-Function-Specifications.md)** - Detailed function specifications with algorithms
10. **[Function Implementation Checklist](./10-Function-Implementation-Checklist.md)** - Implementation tracking checklist
11. **[Duplicate Management Strategy](./11-Duplicate-Management-Strategy.md)** - Multi-repository duplicate handling
12. **[VAR Name Unique Key Design](./12-VAR-Name-Unique-Key-Design.md)** - Design rationale for unique key
13. **[Simplified Duplicate Resolution](./13-Simplified-Duplicate-Resolution.md)** - Simplified algorithms
14. **[Implementation Readiness Review](./14-Implementation-Readiness-Review.md)** - ✅ Readiness assessment

## Quick Start

### Prerequisites

- .NET 9.0 SDK
- PostgreSQL 14+ or Docker with PostgreSQL
- Windows 10/11 (for symbolic link support)
- Visual Studio 2022 or Rider

### Technology Stack

- **Framework**: .NET 9.0
- **UI Framework**: TBD (WPF or Avalonia UI)
- **Database**: PostgreSQL 14+
- **ORM**: Entity Framework Core 9.0
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection
- **Logging**: Serilog
- **Configuration**: appsettings.json

## Core Concepts

### Repository

A **Repository** is a storage location containing VAR files. The system supports multiple repositories:
- Local drives (C:\, D:\, etc.)
- Network drives
- External storage devices
- Each repository can be enabled/disabled
- Priority-based search order

### Installation Target

An **Installation Target** is a directory where symbolic links are created to install VAR files. Supports:
- Multiple target directories
- Profile-based switching
- Active/inactive states

### VAR File

A VAR file is a ZIP archive containing VaM package content with metadata in `meta.json`:
- Format: `CreatorName.PackageName.Version.var`
- Contains: Assets, scripts, scenes, textures, etc.
- Metadata: Dependencies, license, description, preview images

### Symbolic Links

The system uses Windows symbolic links to:
- Link VAR files from repositories to installation targets
- Support cross-drive linking
- Enable fast installation/uninstallation
- Minimize disk space usage

## Architecture Principles

1. **Clean Architecture**: Separation of concerns with clear layer boundaries
2. **SOLID Principles**: Single responsibility, dependency inversion, etc.
3. **Async First**: All I/O operations use async/await
4. **Testability**: Dependency injection and interface-based design
5. **Scalability**: Designed to handle 10,000+ VAR files efficiently
6. **Maintainability**: Modular design with clear documentation

## Project Structure

```
VirtaMatePackageManager/
├── src/
│   ├── VirtaMatePackageManager.Core/        # Domain models, entities
│   ├── VirtaMatePackageManager.Application/ # Business logic, use cases
│   ├── VirtaMatePackageManager.Infrastructure/ # Data access, file operations
│   ├── VirtaMatePackageManager.Presentation/ # UI layer
│   └── VirtaMatePackageManager.API/        # Optional API layer
├── tests/
│   ├── Unit.Tests/
│   ├── Integration.Tests/
│   └── E2E.Tests/
├── docs/
│   └── VirtaMatePackageManager/            # This documentation
└── scripts/
    ├── database/                           # Database migration scripts
    └── deployment/                         # Deployment scripts
```

## Getting Started

1. Review [Architecture Overview](./01-Architecture-Overview.md)
2. Set up [Development Environment](./07-Development-Guide.md#environment-setup)
3. Review [Database Schema](./02-Database-Schema.md)
4. Follow [Development Guide](./07-Development-Guide.md)

## Contributing

Please refer to the development guide for coding standards, git workflow, and contribution guidelines.

## License

TBD

---

**Last Updated**: 2024  
**Documentation Version**: 1.0

