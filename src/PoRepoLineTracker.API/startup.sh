#!/bin/bash
# Azure App Service startup script for PoRepoLineTracker
# Ensures git is available in the container before starting the .NET application.
# The built-in DOTNETCORE|10.0 runtime image does not include git by default.

set -e

# Install git if not already present
if ! command -v git &> /dev/null; then
    echo "[startup] git not found — installing via apt-get..."
    apt-get update -qq
    apt-get install -y --no-install-recommends git
    rm -rf /var/lib/apt/lists/*
    echo "[startup] git installed: $(git --version)"
else
    echo "[startup] git already available: $(git --version)"
fi

# Configure git safe directory for the repos path (avoids 'dubious ownership' errors)
git config --global --add safe.directory '*'

echo "[startup] Starting PoRepoLineTracker.Api..."
exec dotnet /home/site/wwwroot/PoRepoLineTracker.Api.dll
