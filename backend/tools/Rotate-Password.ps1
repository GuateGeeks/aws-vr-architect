[CmdletBinding()]
param([Parameter(Mandatory)][string]$Profile, [string]$Region = 'us-east-1', [string]$StackName = 'guategeeks-aws2026')
$ErrorActionPreference = 'Stop'
# GenerateSecretString is only used at creation. Rotation writes a new AWSCURRENT version.
$python = Join-Path (Split-Path $PSScriptRoot -Parent) '.venv/Scripts/python.exe'
if (-not (Test-Path -LiteralPath $python)) { throw 'Crea .venv e instala requirements-dev.txt primero.' }
& $python (Join-Path $PSScriptRoot 'rotate_password.py') --profile $Profile --region $Region --stack $StackName
if ($LASTEXITCODE -ne 0) { throw 'Falló la rotación.' }
