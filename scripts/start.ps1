﻿# =====================================================================================
#  一键启动脚本
#  -----------------------------------------------------------------------------------
#  作用：
#    1. 如果 wwwroot 里还没有前端构建产物，就先执行 npm install / npm run build；
#    2. 启动后端并自动打开浏览器。
#
#  用法：
#    .\scripts\start.ps1
#  如果提示「禁止运行脚本」，可临时放开：
#    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
# =====================================================================================

$ErrorActionPreference = 'Stop'

# 项目根目录（脚本所在目录的上一级）
$root = Split-Path -Parent $PSScriptRoot
$backendDir = Join-Path $root 'backend\McpDesk.Api'
$frontendDir = Join-Path $root 'frontend'
$indexFile = Join-Path $backendDir 'wwwroot\index.html'

Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  McpDesk 个人 MCP 服务台' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan

# 1. 确保前端已构建
if (-not (Test-Path $indexFile)) {
    Write-Host '[1/2] 未发现前端构建产物，开始构建前端…' -ForegroundColor Yellow

    Push-Location $frontendDir
    try {
        if (-not (Test-Path 'node_modules')) {
            Write-Host '      安装依赖（npm install）…'
            npm install --registry=https://registry.npmmirror.com
        }

        Write-Host '      构建前端（npm run build）…'
        npm run build
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host '[1/2] 已发现前端构建产物，跳过构建。' -ForegroundColor Green
}

# 2. 启动后端
Write-Host '[2/2] 启动后端：http://localhost:5080' -ForegroundColor Green
Write-Host '      按 Ctrl+C 停止服务。' -ForegroundColor DarkGray

Start-Process 'http://localhost:5080' | Out-Null

Push-Location $backendDir
try {
    dotnet run
}
finally {
    Pop-Location
}
