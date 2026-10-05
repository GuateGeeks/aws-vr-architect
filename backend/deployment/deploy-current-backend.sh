#!/usr/bin/env bash
set -euo pipefail
export PATH="$HOME/.local/bin:$PATH"
export AWS_PAGER=''
export SAM_CLI_TELEMETRY=0
DEPLOY_ACCOUNT=$(aws sts get-caller-identity --profile awsday --region us-east-1 --query Account --output text)
[[ "$DEPLOY_ACCOUNT" == '590183968738' ]] || { echo 'Unexpected AWS account; deployment stopped.'; exit 1; }
printf 'Deploying guategeeks-aws2026 in us-east-1; authenticated access from any IP\n'
DEPLOY_PARAMS=("DemoPrefix=ggawsday" "MonthlyBudgetUsd=10" "OpenAIRealtimeModel=${OPENAI_REALTIME_MODEL:-gpt-realtime-2.1}")
if [[ -n "${OPENAI_SECRET_ARN:-}" ]]; then DEPLOY_PARAMS+=("OpenAISecretArn=$OPENAI_SECRET_ARN"); fi
sam deploy --template-file .aws-sam/build/template.yaml --stack-name guategeeks-aws2026 --profile awsday --region us-east-1 --s3-bucket guategeeks-aws2026-artifacts-590183968738-us-east-1 --capabilities CAPABILITY_IAM --no-confirm-changeset --no-fail-on-empty-changeset --parameter-overrides "${DEPLOY_PARAMS[@]}"
