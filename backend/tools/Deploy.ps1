[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Profile,
    [ValidateSet('us-east-1','us-west-2')][string]$Region = 'us-east-1',
    [ValidatePattern('^[a-z][a-z0-9]{2,11}$')][string]$DemoPrefix = 'ggawsday',
    [string]$StackName = 'guategeeks-aws2026',
    [string]$BudgetEmail = '',
    [int]$MonthlyBudgetUsd = 10,
    [switch]$UseContainer
)
$ErrorActionPreference = 'Stop'
foreach ($command in @('aws','sam')) { if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Falta $command. Consulta README.md." } }
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    aws sts get-caller-identity --profile $Profile --region $Region
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo verificar la sesión AWS.' }
    sam validate --lint --template-file template.json --region $Region
    if ($LASTEXITCODE -ne 0) { throw 'Falló la validación SAM.' }
    $buildArgs = @('build','--template-file','template.json')
    if ($UseContainer) { $buildArgs += '--use-container' }
    & sam @buildArgs
    if ($LASTEXITCODE -ne 0) { throw 'Falló SAM build. Usa Python 3.13 o -UseContainer con Docker.' }
    $parameters = @("DemoPrefix=$DemoPrefix", "MonthlyBudgetUsd=$MonthlyBudgetUsd")
    if ($BudgetEmail) { $parameters += "BudgetEmail=$BudgetEmail" }
    sam deploy --template-file .aws-sam/build/template.yaml --stack-name $StackName --profile $Profile --region $Region --resolve-s3 --capabilities CAPABILITY_IAM --no-fail-on-empty-changeset --parameter-overrides @parameters
    if ($LASTEXITCODE -ne 0) { throw 'Falló el despliegue; consulta los eventos de CloudFormation.' }
} finally { Pop-Location }
