#!/usr/bin/env bash
set -euo pipefail

echo "Building frontend..."
cd frontend
npm install
npm run build

echo "Frontend build complete."
echo "Run the backend with: cd backend && dotnet run"
