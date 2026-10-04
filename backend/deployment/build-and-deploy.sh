#!/usr/bin/env bash
set -euo pipefail
export PATH="$HOME/.venvs/guategeeks-aws2026/bin:$HOME/.local/bin:$PATH"
export SAM_CLI_TELEMETRY=0
cd /mnt/c/Users/adawolfs/Unity/GuateGeeksAWS2026
sam build --template-file template.json
bash deployment/deploy-current-backend.sh
