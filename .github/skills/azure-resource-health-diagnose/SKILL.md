---
name: azure-resource-health-diagnose
description: 'Analyze Azure resource health, diagnose issues from logs and telemetry, and create a remediation plan for identified problems.'
---

# Azure Resource Health & Issue Diagnosis

This workflow analyzes a specific Azure resource to assess its health status, diagnose potential issues using logs and telemetry data, and develop a comprehensive remediation plan.

## Prerequisites

- Azure CLI configured and authenticated
- Target Azure resource identified (name and optionally resource group/subscription)
- Resource must be deployed and running to generate logs/telemetry

## Workflow Steps

### Step 1: Resource Discovery & Identification

1. **Resource Lookup**:
   - If only resource name provided: Search across subscriptions
   - Gather: Resource type, current status, location, tags, configuration
   - Identify associated services and dependencies

2. **Resource Type Detection**:
   - **Web Apps/Function Apps**: Application logs, performance metrics
   - **Virtual Machines**: System logs, performance counters
   - **Cosmos DB**: Request metrics, throttling
   - **Storage Accounts**: Access logs, performance metrics
   - **SQL Database**: Query performance, connection logs
   - **Application Insights**: Application telemetry, exceptions
   - **Key Vault**: Access logs, certificate status

### Step 2: Health Status Assessment

- Check resource provisioning state and operational status
- Verify service availability and responsiveness
- Review recent deployment or configuration changes
- Assess current resource utilization (CPU, memory, storage)

### Step 3: Log & Telemetry Analysis

1. **Find Monitoring Sources**:
   - Identify Log Analytics workspaces
   - Locate Application Insights instances
   - Identify relevant log tables

2. **Execute Diagnostic Queries**:
   - General Error Analysis: Recent errors and exceptions
   - Performance Analysis: Performance degradation patterns
   - Application-Specific: Failed requests, connection failures

3. **Pattern Recognition**:
   - Identify recurring error patterns or anomalies
   - Correlate errors with deployment times or configuration changes
   - Analyze performance trends and degradation patterns

### Step 4: Issue Classification & Root Cause Analysis

1. **Issue Classification**:
   - **Critical**: Service unavailable, data loss, security breaches
   - **High**: Performance degradation, intermittent failures
   - **Medium**: Warnings, suboptimal configuration
   - **Low**: Informational alerts, optimization opportunities

2. **Root Cause Analysis**:
   - Configuration Issues, Resource Constraints, Network Issues
   - Application Issues, External Dependencies, Security Issues

### Step 5: Generate Remediation Plan

1. **Immediate Actions** (Critical): Emergency fixes to restore service
2. **Short-term Fixes** (High/Medium): Configuration adjustments, scaling
3. **Long-term Improvements**: Architectural changes, preventive measures

### Step 6: User Confirmation & Report Generation

Present findings summary and get approval for remediation actions. Generate a detailed health report with:
- Executive Summary
- Health Metrics
- Issues Identified (by severity)
- Remediation Plan (phased)
- Monitoring Recommendations
- Validation Steps
- Prevention Measures
