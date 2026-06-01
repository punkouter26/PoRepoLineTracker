---
name: appinsights-instrumentation
description: 'Instrument a webapp to send useful telemetry data to Azure App Insights'
---

# AppInsights Instrumentation

This skill enables sending telemetry data of a webapp to Azure App Insights for better observability of the app's health.

## When to Use This Skill

Use this skill when the user wants to enable telemetry for their webapp.

## Prerequisites

The app in the workspace must be one of these kinds:
- An ASP.NET Core app hosted in Azure
- A Node.js app hosted in Azure

## Guidelines

### Collect Context Information

Find out the (programming language, application framework, hosting) tuple of the application. Read the source code to make an educated guess. Confirm with the user on anything you don't know.

### Prefer Auto-Instrument if Possible

If the app is a C# ASP.NET Core app hosted in Azure App Service, use auto-instrumentation via the Azure portal or `az monitor app-insights` commands.

### Manually Instrument

#### Create AppInsights Resource

- Add AppInsights to existing Bicep template (preferred if Bicep files exist)
- Use Azure CLI to create the App Insights resource
- Create in the same resource group as the hosted app

#### Modify Application Code

- **ASP.NET Core**: Add `Azure.Monitor.OpenTelemetry.AspNetCore` package, configure `AddOpenTelemetry().UseAzureMonitor()` in Program.cs
- **Node.js**: Add `applicationinsights` package, configure with connection string
- **Python**: Add `opencensus-ext-azure` package, configure exporter

### Verify Instrumentation

- Confirm the connection string is configured
- Verify telemetry is flowing to the App Insights resource
- Check that custom metrics, traces, and exceptions are being captured
