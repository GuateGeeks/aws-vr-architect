# AWS setup commands — WSL / Bash

Run these blocks **one section at a time inside your WSL Bash terminal**. Package installation below assumes Ubuntu/Debian. Use Linux `aws`, `sam` and Python, not `aws.exe` or the Windows `.venv`. Choose one authentication method in section 3. Section 6 creates AWS resources; cleanup at the end deletes demo data.

Verified on your workstation: **Ubuntu 24.04.4 under WSL 2**, Linux **AWS CLI 2.37.7**, **SAM CLI 1.166.2**, and **Python 3.13.13**. AWS and SAM are available in `~/.local/bin`; project dependencies are installed in `~/.venvs/guategeeks-aws2026`. Skip their installation blocks on this workstation. If your terminal uses Zsh, run `bash` before copying these Bash blocks.

The authorized live tests used the existing browser login through a separate `awsday-test` profile with explicit `us-east-1` and a credential process. That login is **root**; creating the tool profile did not change its identity or repair the existing `default` profile. This guide retains the operator-profile setup for routine deployment. Docker remains unavailable, but native Python 3.13 SAM builds and live deployment succeeded. See the [WSL verification report](docs/wsl-verification.md).

## 1. Select the project and verify AWS CLI

Keep the same terminal for subsequent sections. If you open another, repeat this section and the identity verification in section 4. These shell variables contain configuration, not secrets.

```bash
cd /mnt/c/Users/adawolfs/Unity/GuateGeeksAWS2026
set -euo pipefail

DEMO_PROFILE=awsday
DEMO_LOGIN_PROFILE=awsday-login
DEMO_REGION=us-east-1
DEMO_STACK=guategeeks-aws2026
DEMO_PREFIX=ggawsday
export AWS_PAGER=''
export PATH="$HOME/.local/bin:$PATH"
read -rp 'AWS account ID (12 digits): ' DEMO_ACCOUNT_ID
[[ "$DEMO_ACCOUNT_ID" =~ ^[0-9]{12}$ ]] || { echo 'Invalid account ID'; exit 1; }

cat /etc/os-release
uname -m
command -v aws || true
```

The project lives on the Windows drive, mounted as `/mnt/c` in WSL. AWS profiles and login caches will belong to your **Linux user** under `~/.aws`; a Windows AWS installation/profile is separate. Run login/configuration as your normal Linux user, without `sudo`.

## 2. Check/install Linux prerequisites

```bash
sudo apt-get update
sudo apt-get install -y ca-certificates curl unzip python3 python3-venv python3-pip less groff
```

If Linux AWS CLI v2 is already installed, skip the installation block and run `aws --version`. Browser login in section 3A requires v2.32.0 or newer. This block installs/updates AWS CLI at the standard Linux location:

```bash
DEMO_AWS_INSTALL_DIR=$(mktemp -d /tmp/ggaws-cli.XXXXXX)
case "$(uname -m)" in
  x86_64) DEMO_AWS_ARCH=x86_64 ;;
  aarch64|arm64) DEMO_AWS_ARCH=aarch64 ;;
  *) echo 'Unsupported CPU architecture'; exit 1 ;;
esac
curl -fsSL "https://awscli.amazonaws.com/awscli-exe-linux-${DEMO_AWS_ARCH}.zip" -o "$DEMO_AWS_INSTALL_DIR/awscliv2.zip"
unzip -q "$DEMO_AWS_INSTALL_DIR/awscliv2.zip" -d "$DEMO_AWS_INSTALL_DIR"
if [[ -d /usr/local/aws-cli ]]; then
  sudo "$DEMO_AWS_INSTALL_DIR/aws/install" --bin-dir /usr/local/bin --install-dir /usr/local/aws-cli --update
else
  sudo "$DEMO_AWS_INSTALL_DIR/aws/install" --bin-dir /usr/local/bin --install-dir /usr/local/aws-cli
fi
export PATH="/usr/local/bin:$PATH"
hash -r
```

Check that the executable resolves to Linux and reports `aws-cli/2...`:

```bash
command -v aws
aws --version
aws configure list-profiles
```

[Official AWS CLI installation instructions and signature verification](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html).

Before signing in, enable MFA and prepare an operator identity in the AWS console with permission to deploy this project's resources and IAM roles. If the new account only has its root login, configure that operator identity in the console first. CLI login authenticates the selected identity; it does not grant deployment permissions.

## 3A. Browser login — IAM or federated console identity

Use this if you sign into the console without an IAM Identity Center portal. Your operator needs `SignInLocalDevelopmentAccess` or equivalent permission, plus deployment permissions. The remote flow prints a URL: open it in your Windows browser, sign into the intended account as the operator, then paste the returned authorization code into the **WSL prompt**, not a command or this file.

```bash
aws configure set region "$DEMO_REGION" --profile "$DEMO_LOGIN_PROFILE"
aws configure set output json --profile "$DEMO_LOGIN_PROFILE"
aws login --profile "$DEMO_LOGIN_PROFILE" --remote

# Different profile names avoid credential-provider recursion.
[[ "$DEMO_PROFILE" != "$DEMO_LOGIN_PROFILE" ]]
DEMO_AWS_EXECUTABLE=$(command -v aws)
DEMO_CREDENTIAL_PROCESS="$DEMO_AWS_EXECUTABLE configure export-credentials --profile $DEMO_LOGIN_PROFILE --format process --region $DEMO_REGION"
aws configure set credential_process "$DEMO_CREDENTIAL_PROCESS" --profile "$DEMO_PROFILE"
aws configure set region "$DEMO_REGION" --profile "$DEMO_PROFILE"
aws configure set output json --profile "$DEMO_PROFILE"
```

The separate tool profile lets this project's pinned boto3 and SAM obtain temporary credentials through AWS CLI. Do not run `export-credentials` directly or paste its output: it contains secrets. Use fresh profile names if these names already contain another authentication configuration. Skip 3B and continue to section 4. [AWS browser login and credential-process compatibility](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-sign-in.html).

## 3B. Alternative: IAM Identity Center / SSO

Use this **instead of 3A** if you have an SSO start/issuer URL and an assigned account/permission set. The configuration wizard cannot create Identity Center or grant assignments; those must exist first. The device flow works with your Windows browser.

```bash
aws configure sso --profile "$DEMO_PROFILE" --use-device-code --no-browser
aws sso login --profile "$DEMO_PROFILE" --use-device-code --no-browser
aws configure set region "$DEMO_REGION" --profile "$DEMO_PROFILE"
aws configure set output json --profile "$DEMO_PROFILE"
```

Wizard values: session name `awsday-sso`, your actual start/issuer URL, the region hosting Identity Center, registration scopes `sso:account:access`, then your AWS account and assigned deployment role. The Identity Center region can differ from `us-east-1`. Open the displayed URL and enter the device code when prompted. [AWS SSO configuration](https://docs.aws.amazon.com/cli/latest/userguide/cli-configure-sso.html).

## 4. Verify account and permissions

```bash
aws configure list --profile "$DEMO_PROFILE"
aws sts get-caller-identity --profile "$DEMO_PROFILE" --region "$DEMO_REGION"
DEMO_ACTUAL_ACCOUNT=$(aws sts get-caller-identity --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --query Account --output text)
DEMO_ACTUAL_ARN=$(aws sts get-caller-identity --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --query Arn --output text)
[[ "$DEMO_ACTUAL_ACCOUNT" == "$DEMO_ACCOUNT_ID" ]] || { echo 'Wrong AWS account'; exit 1; }
[[ "$DEMO_ACTUAL_ARN" != *:root ]] || { echo 'Use the deployment operator identity instead of root'; exit 1; }
aws configure get region --profile "$DEMO_PROFILE"
aws lambda get-account-settings --profile "$DEMO_PROFILE" --region "$DEMO_REGION"
```

The bootstrap operator needs permissions for CloudFormation, S3 artifact storage, IAM role/policy creation and PassRole, Lambda, API Gateway, Secrets Manager and CloudWatch. An optional budget requires AWS Budgets permissions. STS verifies identity, not all deployment permissions. The template creates the runtime roles; no IAM access keys are needed for Quest.

## 5. Install SAM and prepare the Linux Python environment

Check for SAM first. If already installed in WSL, skip its installation block.

```bash
command -v sam || true
```

Install the Linux SAM CLI:

```bash
DEMO_SAM_INSTALL_DIR=$(mktemp -d /tmp/ggaws-sam.XXXXXX)
case "$(uname -m)" in
  x86_64) DEMO_SAM_ARCH=x86_64 ;;
  aarch64|arm64) DEMO_SAM_ARCH=arm64 ;;
  *) echo 'Unsupported CPU architecture'; exit 1 ;;
esac
curl -fsSL "https://github.com/aws/aws-sam-cli/releases/latest/download/aws-sam-cli-linux-${DEMO_SAM_ARCH}.zip" -o "$DEMO_SAM_INSTALL_DIR/sam.zip"
unzip -q "$DEMO_SAM_INSTALL_DIR/sam.zip" -d "$DEMO_SAM_INSTALL_DIR/unpacked"
sudo "$DEMO_SAM_INSTALL_DIR/unpacked/install"
hash -r
sam --version
```

If SAM is already installed and needs updating, use the installer's `--update` option. [Official Linux SAM installation](https://docs.aws.amazon.com/serverless-application-model/latest/developerguide/install-sam-cli.html).

Use a dedicated Linux virtual environment outside `/mnt/c` for dependencies. Leave the existing Windows `.venv` intact:

```bash
python3 --version
# Python 3.12 or 3.13 is suitable for the local tools; the Lambda runtime is 3.13.
DEMO_VENV_DIR="$HOME/.venvs/guategeeks-aws2026"
mkdir -p "$HOME/.venvs"
if [[ ! -x "$DEMO_VENV_DIR/bin/python" ]]; then
  python3 -m venv "$DEMO_VENV_DIR"
fi
source "$DEMO_VENV_DIR/bin/activate"
if python -m pip --version >/dev/null 2>&1; then
  python -m pip install -r requirements-dev.txt
else
  uv pip install --python "$DEMO_VENV_DIR/bin/python" -r requirements-dev.txt
fi
python -m unittest discover -s tests -v
python tools/validate_templates.py
```

The build below uses the installed Python 3.13 and does not require Docker. If using another workstation without Python 3.13, either install that runtime or add `--use-container` to the build command after enabling Docker WSL integration. [Docker Desktop WSL integration](https://docs.docker.com/desktop/features/wsl/).

```bash
python3.13 --version
```

Creating a Python 3.12 virtual environment does not supply the 3.13 runtime to SAM. Python 3.13 is already installed on this workstation.

## 6. Restrict the API to your network and deploy

For the backend already deployed on this workstation, use [the current deployment record](deployment/current-backend.md). The shared SAM artifact-bucket reference is stale here; the active installation uses the dedicated private bucket `guategeeks-aws2026-artifacts-590183968738-us-east-1`. Its `deployment/deploy-current-backend.sh` uses `--s3-bucket` explicitly. The generic `--resolve-s3` example below applies to environments with working SAM-managed storage.

Run the IP check from the network that will call the API. It reports this computer's current public egress IP, including VPN routing. The Quest and test computer must use the allowed egress IP. At the event, rerun the IP check and deployment using the same stack and prefix to update the restriction.

```bash
DEMO_PUBLIC_IP=$(curl -4fsS https://checkip.amazonaws.com | tr -d '\r\n')
python -c 'import ipaddress,sys; ipaddress.IPv4Address(sys.argv[1])' "$DEMO_PUBLIC_IP"
DEMO_ALLOWED_CIDR="$DEMO_PUBLIC_IP/32"
printf 'Account: %s\nRegion: %s\nAllowed source: %s\n' "$DEMO_ACTUAL_ACCOUNT" "$DEMO_REGION" "$DEMO_ALLOWED_CIDR"
read -rp 'Budget alert email (empty to skip): ' DEMO_BUDGET_EMAIL

sam validate --lint --template-file template.json --region "$DEMO_REGION" --profile "$DEMO_PROFILE"
DEMO_BUILD_DIR="$HOME/.cache/guategeeks-aws2026/build"
sam build --template-file template.json --build-dir "$DEMO_BUILD_DIR"

DEMO_PARAMETERS=("DemoPrefix=$DEMO_PREFIX" "AllowedCidr=$DEMO_ALLOWED_CIDR" "MonthlyBudgetUsd=10")
if [[ -n "$DEMO_BUDGET_EMAIL" ]]; then
  DEMO_PARAMETERS+=("BudgetEmail=$DEMO_BUDGET_EMAIL")
fi

# Creates billable AWS resources. Review the account and IP printed above first.
sam deploy \
  --template-file "$DEMO_BUILD_DIR/template.yaml" \
  --stack-name "$DEMO_STACK" \
  --profile "$DEMO_PROFILE" \
  --region "$DEMO_REGION" \
  --resolve-s3 \
  --capabilities CAPABILITY_IAM \
  --no-fail-on-empty-changeset \
  --parameter-overrides "${DEMO_PARAMETERS[@]}"
```

These are the Bash equivalents of `tools/Deploy.ps1`; do not run that PowerShell script inside Bash. The optional budget is an account-wide alert at 80% of USD 10, not a spending cap or estimate. Keep `DEMO_PREFIX` unchanged while demo stacks exist.

Build artifacts go on the Linux filesystem: copying SDK dependencies onto `/mnt/c` was substantially slower during verification. Source files remain in the project directory. This native build path was verified without Docker.

## 7. Inspect outputs and run the real demo

```bash
aws cloudformation describe-stacks \
  --stack-name "$DEMO_STACK" --profile "$DEMO_PROFILE" --region "$DEMO_REGION" \
  --query 'Stacks[0].Outputs' --output table

python tools/demo.py session --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK"

# Each smoke command creates a real architecture and sends a test event.
python tools/demo.py smoke --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK" --slot 1 --architecture examples/api-serverless.json
python tools/demo.py smoke --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK" --slot 2 --architecture examples/eventos-cola.json
python tools/demo.py smoke --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK" --slot 3 --architecture examples/procesar-archivos.json
```

The helper retrieves Basic Auth credentials from Secrets Manager in memory without printing them. Unity 0.3.0 includes the HTTP adapter. Before connecting Quest, run `python tools/check_connection.py --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK"` to verify session/authentication, catalog and server validation without creating architecture resources. Then follow [the VR connection guide](../GuateGeeksAWSVR/Documentation/Cloud-Integration.md) to provision the service-account session through `tools/connect_quest.py`. Operator AWS login and Quest Basic Auth are separate.

## 8. Diagnose problems and renew login

```bash
aws cloudformation describe-stack-events --stack-name "$DEMO_STACK" --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --max-items 20 --output table
aws logs tail "/aws/lambda/$DEMO_PREFIX-control" --since 30m --profile "$DEMO_PROFILE" --region "$DEMO_REGION"

# Replace 1 with the failing architecture's slot.
aws cloudformation describe-stack-events --stack-name "$DEMO_PREFIX-demo-1" --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --max-items 20 --output table
```

For expired credentials, run **only your selected method**, then repeat section 4:

```bash
# Option A only:
aws login --profile "$DEMO_LOGIN_PROFILE" --remote
```

```bash
# Option B only:
aws sso login --profile "$DEMO_PROFILE" --use-device-code --no-browser
```

If credentials resolve incorrectly, `aws configure list --profile "$DEMO_PROFILE"` shows their source with secrets masked. Use fresh dedicated profile names if a profile contains old credentials. If this terminal inherited AWS keys from an unrelated session, clear those session variables before retrying:

```bash
unset AWS_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY AWS_SESSION_TOKEN
```

API 403: check egress IP/VPN and rerun section 6. API 401: refresh the configured Basic Auth secret. API 409: inspect the occupied slot and wait for its current operation. After restarting WSL, activate the virtual environment again with `source "$HOME/.venvs/guategeeks-aws2026/bin/activate"`.

## 9. Rotate the API password if keeping the backend

```bash
python tools/rotate_password.py --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK"
```

Retrieve the new password securely and update clients. The API's credential cache can retain the previous password for up to 60 seconds.

## 10. Cleanup — run only when the demo is finished

Stop clients and event producers first. These commands delete architecture resources and S3 data, including object versions. Keep the API reachable until all slots are gone. The loop stops on a failure so the control plane remains available for recovery.

```bash
for DEMO_SLOT in 1 2 3; do
  python tools/demo.py delete --profile "$DEMO_PROFILE" --region "$DEMO_REGION" --stack "$DEMO_STACK" --slot "$DEMO_SLOT" || {
    echo "Slot $DEMO_SLOT failed to delete; keep the control plane and inspect CloudFormation."
    exit 1
  }
done
sam delete --stack-name "$DEMO_STACK" --profile "$DEMO_PROFILE" --region "$DEMO_REGION"
```

The control-plane deletion removes its Basic Auth secret without a recovery window. Check for retained/shared SAM artifact storage separately; do not delete a shared bucket without checking its other users.

Sign out using **only your authentication method**:

```bash
# Option A only:
aws logout --profile "$DEMO_LOGIN_PROFILE"
```

```bash
# Option B only: clears cached SSO sessions across profiles.
aws sso logout
```
