#!/usr/bin/env bash
set -euo pipefail
export PATH="$HOME/.local/bin:$PATH"
export AWS_PAGER=''
export SAM_CLI_TELEMETRY=0
DEPLOY_ACCOUNT=$(aws sts get-caller-identity --profile awsday --region us-east-1 --query Account --output text)
[[ "$DEPLOY_ACCOUNT" == '590183968738' ]] || { echo 'Unexpected AWS account; deployment stopped.'; exit 1; }
DEPLOY_IP=$(curl -4fsS https://checkip.amazonaws.com | tr -d '\r\n')
"$HOME/.venvs/guategeeks-aws2026/bin/python" -c 'import ipaddress,sys; ipaddress.IPv4Address(sys.argv[1])' "$DEPLOY_IP"
printf 'Deploying guategeeks-aws2026 in us-east-1; allowed source %s/32\n' "$DEPLOY_IP"
DEPLOY_PARAMS=("DemoPrefix=ggawsday" "AllowedCidr=$DEPLOY_IP/32" "MonthlyBudgetUsd=10")
if [[ -n "${OPENAI_SECRET_ARN:-}" ]]; then DEPLOY_PARAMS+=("OpenAISecretArn=$OPENAI_SECRET_ARN"); fi
sam deploy --template-file .aws-sam/build/template.yaml --stack-name guategeeks-aws2026 --profile awsday --region us-east-1 --s3-bucket guategeeks-aws2026-artifacts-590183968738-us-east-1 --capabilities CAPABILITY_IAM --no-confirm-changeset --no-fail-on-empty-changeset --parameter-overrides "${DEPLOY_PARAMS[@]}"
