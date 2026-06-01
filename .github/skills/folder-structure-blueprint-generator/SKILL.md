---
name: folder-structure-blueprint-generator
description: 'Comprehensive technology-agnostic prompt for analyzing and documenting project folder structures. Auto-detects project types (.NET, Java, React, Angular, Python, Node.js, Flutter), generates detailed blueprints with visualization options, naming conventions, file placement patterns, and extension templates for maintaining consistent code organization across diverse technology stacks.'
---

# Folder Structure Blueprint Generator

Analyze the project's folder structure and create a comprehensive `Project_Folders_Structure_Blueprint.md` document that serves as a definitive guide for maintaining consistent code organization.

## Workflow

### 1. Initial Auto-Detect Phase

Scan the folder structure for key files that identify the project type:
- Look for solution/project files (`.sln`, `.csproj`) to identify .NET projects
- Check for build files (`pom.xml`, `build.gradle`) for Java projects
- Identify `package.json` with dependencies for JavaScript/TypeScript projects
- Check for Python project identifiers (`requirements.txt`, `setup.py`, `pyproject.toml`)
- Note all technology signatures found and their versions

### 2. Structural Overview

- Document the overall architectural approach reflected in the folder structure
- Identify the main organizational principles (by feature, by layer, by domain, etc.)
- Note any structural patterns that repeat throughout the codebase

### 3. Directory Visualization

Create a tree representation of the folder hierarchy showing:
- All significant directories and their nesting
- Key files in each directory
- Purpose of each directory

### 4. Key Directory Analysis

Document each significant directory's purpose, contents, and patterns:
- Source code organization approach
- Configuration file locations
- Test project organization
- Resource organization
- Build and output organization

### 5. File Placement Patterns

Document the patterns that determine where different types of files should be placed:
- Configuration files
- Model/Entity definitions
- Business logic
- Interface definitions
- Test files
- Documentation files

### 6. Naming and Organization Conventions

- File naming patterns (case conventions, prefixes, suffixes)
- Folder naming patterns
- Namespace/Module patterns
- Organizational patterns (code co-location, feature encapsulation)

### 7. Extension and Evolution

- How to add new modules/features while maintaining conventions
- Plugin/extension folder patterns
- Scalability patterns

### 8. Structure Enforcement

- Tools/scripts that enforce structure
- Build checks for structural compliance
- Documentation practices
