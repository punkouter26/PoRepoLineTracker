# Update all Mermaid diagrams to SVG format
Write-Host "Updating Mermaid diagrams..." -ForegroundColor Green

# Define diagram files
$diagrams = @(
    "Diagrams/project-dependencies.mmd",
    "Diagrams/domain-model.mmd",
    "Diagrams/feature-sequence.mmd",
    "Diagrams/user-workflow.mmd",
    "Diagrams/domain-model-simple.mmd"
)

# Check if mermaid CLI is available
try {
    npx mmdc --version > $null 2>&1
    Write-Host "Mermaid CLI found" -ForegroundColor Green
}
catch {
    Write-Host "Mermaid CLI not found. Installing..." -ForegroundColor Yellow
    npm install -g @mermaid-js/mermaid-cli
}

# Generate SVG files for each diagram
foreach ($diagram in $diagrams) {
    if (Test-Path $diagram) {
        $svgFile = $diagram -replace '\.mmd$', '.svg'
        Write-Host "Updating $svgFile..." -ForegroundColor Cyan
        
        try {
            npx mmdc -i $diagram -o $svgFile
            Write-Host "✓ Updated $svgFile" -ForegroundColor Green
        }
        catch {
            Write-Host "✗ Failed to update $svgFile" -ForegroundColor Red
        }
    }
    else {
        Write-Host "Diagram file not found: $diagram" -ForegroundColor Yellow
    }
}

Write-Host "Diagram update complete!" -ForegroundColor Green
