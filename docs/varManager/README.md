# varManager-MMDLoader Technical Specification

## Overview

This document provides a comprehensive technical specification for the **varManager-MMDLoader** system, version 1.0.1.0. This system is a package management tool for Virt-a-Mate (VaM), a virtual reality application. The tool manages VAR (VaM Addon Resource) files using a repository-based approach with symbolic links.

## Purpose

varManager-MMDLoader is designed to:
- Manage VAR package files for Virt-a-Mate
- Organize VAR files in a centralized repository directory
- Create symbolic links in the AddonPackages directory as needed
- Load and manage MMD (MikuMikuDance) motion files into VaM scenes
- Analyze VAR dependencies and manage package installations

## Document Structure

This technical specification is organized into the following phases:

1. **[Architecture Overview](./01-Architecture-Overview.md)** - Solution structure, project organization, and architectural patterns
2. **[varManager Core Application](./02-varManager-Core.md)** - Main Windows Forms application for VAR management
3. **[MMDLoader Application](./03-MMDLoader.md)** - WPF application for loading MMD motions
4. **[LoadScene Component](./04-LoadScene.md)** - Unity plugin for scene loading functionality
5. **[UI Components](./05-UI-Components.md)** - Reusable UI controls and components
6. **[Database Schema](./06-Database-Schema.md)** - Database structure and data models
7. **[Build Configuration](./07-Build-Configuration.md)** - Build system, dependencies, and deployment

## Technology Stack

- **Primary Framework**: .NET Framework 4.8 (varManager), .NET 6.0 (MMDLoader)
- **UI Frameworks**: Windows Forms, WPF
- **Database**: Microsoft Access (OLEDB)
- **Architecture**: Multi-project solution with component separation
- **Language**: C#

## Key Features

### varManager
- VAR file repository management
- Symbolic link creation and management
- Package dependency analysis
- Installation status tracking
- Missing VAR detection
- Stale VAR cleanup
- Scene analysis and preparation
- Hub integration for package browsing

### MMDLoader
- MMD project directory scanning
- VMD (motion) and audio file management
- Multiple person/character motion support
- Camera motion integration
- Timeline generation for VaM scenes
- Real-time audio preview

### LoadScene
- Unity plugin for scene loading
- MMD model and motion parsing
- Timeline integration
- Bone mapping and morph support

## Version Information

- **Documentation Version**: 1.0
- **Source Code Version**: 1.0.1.0
- **Last Updated**: 2024

## Navigation

Use the following links to navigate through the documentation:

1. [Architecture Overview](./01-Architecture-Overview.md)
2. [varManager Core Application](./02-varManager-Core.md)
3. [MMDLoader Application](./03-MMDLoader.md)
4. [LoadScene Component](./04-LoadScene.md)
5. [UI Components](./05-UI-Components.md)
6. [Database Schema](./06-Database-Schema.md)
7. [Build Configuration](./07-Build-Configuration.md)

## Critical Analysis

- **[Criticism Document](./Criticism-Document.md)** - Comprehensive analysis of critical issues, performance problems, and architectural flaws in the varManager system

---

*This documentation was generated through systematic code review and analysis of the varManager-MMDLoader source code.*

