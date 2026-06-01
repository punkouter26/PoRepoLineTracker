---
name: architecture-blueprint-generator
description: 'Comprehensive project architecture blueprint generator that analyzes codebases to create detailed architectural documentation. Automatically detects technology stacks and architectural patterns, generates visual diagrams, documents implementation patterns, and provides extensible blueprints for maintaining architectural consistency and guiding new development.'
---

# Architecture Blueprint Generator

Create a comprehensive `Project_Architecture_Blueprint.md` document that thoroughly analyzes the architectural patterns in the codebase to serve as a definitive reference for maintaining architectural consistency.

## Workflow

### 1. Architecture Detection and Analysis

- Analyze the project structure to identify all technology stacks and frameworks in use by examining project files, package dependencies, import statements, and framework-specific patterns.
- Determine the architectural pattern(s) by analyzing folder organization, dependency flow, interface segregation, and communication mechanisms.

### 2. Architectural Overview

- Provide a clear, concise explanation of the overall architectural approach.
- Document the guiding principles evident in the architectural choices.
- Identify architectural boundaries and how they're enforced.

### 3. Architecture Visualization

Create diagrams at multiple levels of abstraction:
- High-level architectural overview showing major subsystems
- Component interaction diagrams showing relationships and dependencies
- Data flow diagrams showing how information moves through the system

### 4. Core Architectural Components

For each architectural component:
- **Purpose and Responsibility**: Primary function, business domains addressed
- **Internal Structure**: Organization of classes/modules, key abstractions
- **Interaction Patterns**: How the component communicates with others

### 5. Architectural Layers and Dependencies

- Map the layer structure as implemented in the codebase
- Document the dependency rules between layers
- Identify abstraction mechanisms that enable layer separation
- Note any circular dependencies or layer violations

### 6. Data Architecture

- Document domain model structure and organization
- Map entity relationships and aggregation patterns
- Identify data access patterns (repositories, data mappers, etc.)
- Document data transformation and mapping approaches

### 7. Cross-Cutting Concerns

Document implementation patterns for:
- **Authentication & Authorization**: Security model, permission enforcement
- **Error Handling & Resilience**: Exception handling, retry, circuit breaker
- **Logging & Monitoring**: Instrumentation, observability
- **Validation**: Input validation, business rule validation
- **Configuration Management**: Configuration sources, secret management

### 8. Technology-Specific Patterns

Document patterns specific to the detected technology stack (.NET, Java, React, Angular, Python, Node.js, etc.).

### 9. Testing Architecture

- Document testing strategies aligned with the architecture
- Identify test boundary patterns (unit, integration, system)
- Map test doubles and mocking approaches

### 10. Deployment Architecture

- Document deployment topology derived from configuration
- Identify environment-specific architectural adaptations
- Map runtime dependency resolution patterns
