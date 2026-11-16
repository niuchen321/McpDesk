﻿# =====================================================================================
#  构建脚本：构建前端 + 编译后端
#  -----------------------------------------------------------------------------------
#  用法：
#    .\scripts\build-all.ps1
#
#  产物：
#    · 前端：backend\McpDesk.Api\wwwroot\（由 Vite 直接输出）
#    · 后端：backend\McpDesk.Api\bin\Release\net8.0\
# =====================================================================================

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$frontendDir = Join-Path $root 'frontend'
$backendProject = Join-Path $root 'backend\McpDesk.Api\McpDesk.Api.csproj'

Write-Host '=== 1/2 构建前端 ===' -ForegroundColor Cyan
Push-Location $frontendDir
try {
    if (-not (Test-Path 'node_modules')) {
        npm install --registry=https://registry.npmmirror.com
    }

    npm run build
}
finally {
    Pop-Location
}

Write-Host '=== 2/2 编译后端 ===' -ForegroundColor Cyan
dotnet build $backendProject -c Release

Write-Host ''
Write-Host '构建完成。运行：dotnet run --project backend\McpDesk.Api -c Release --urls http://localhost:5080' -ForegroundColor Green
