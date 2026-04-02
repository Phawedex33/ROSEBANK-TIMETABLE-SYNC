$ErrorActionPreference = "Stop"

Write-Host "Building frontend..."
Push-Location frontend
npm install
npm run build
Pop-Location

Write-Host "Frontend build complete."
Write-Host "Run the backend with: Set-Location backend; dotnet run"
