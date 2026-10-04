# WSL verification — October 1, 2026

Tests, SAM build, deployment and AWS integration tests ran inside Ubuntu WSL using Linux executables. This replaces the initial read-only report: dependencies were installed and temporary cloud resources were created for the authorized tests.

| Check | Result |
|---|---|
| Distribution | Ubuntu 24.04.4 LTS, WSL 2, x86_64 |
| AWS CLI | 2.37.7, `~/.local/bin/aws` |
| SAM CLI | 1.166.2, `~/.local/bin/sam` |
| Build/test Python | 3.13.13, installed through the existing uv tool |
| Linux virtual environment | `~/.venvs/guategeeks-aws2026` |
| Dependencies | boto3 1.40.45, cfn-lint 1.40.2, PyYAML 6.0.2 |
| Unit tests | 35 passed, including three source-IP regression tests |
| CloudFormation validation | 11 templates, zero findings |
| SAM | Validation and native Python 3.13 build passed |
| Docker | Unavailable to this distro; not needed for the native build |
| AWS region | us-east-1 |

Build artifacts are under `~/.cache/guategeeks-aws2026/build`; copying dependencies onto `/mnt/c` was substantially slower. See [validation.md](validation.md) for live-test results and coverage limits.

Live password rotation passed after credential-cache expiry. The temporary cloud stacks and dedicated artifact bucket were deleted, and service inventories verified cleanup. The Linux tools and `awsday-test` profile remain available; no permanent backend was left running.

## Authentication used for testing

The existing `default` profile uses browser login as root. Its region was empty and its configured credential process had a missing profile argument. AWS CLI could authenticate with an explicit region, but pinned boto3 could not use that configuration directly.

A separate `awsday-test` profile was configured with region `us-east-1` and this credential process. The SDK consumes its output privately; do not run it to print credentials:

```text
/home/adawolfs/.local/bin/aws configure export-credentials --profile default --format process --region us-east-1
```

The absolute path supports noninteractive shells; the explicit region also permits session refresh. The default profile was left unchanged. The new profile still uses root; it is not a new operator identity. No permanent access keys were created. Follow [AWS-SETUP-COMMANDS.md](../AWS-SETUP-COMMANDS.md) for routine operator setup.

## Repeat local checks

```bash
export PATH="$HOME/.local/bin:$PATH"
source "$HOME/.venvs/guategeeks-aws2026/bin/activate"
cd /mnt/c/Users/adawolfs/Unity/GuateGeeksAWS2026
python -m unittest discover -s tests -v
python tools/validate_templates.py
sam validate --lint --template-file template.json --region us-east-1
sam build --template-file template.json --build-dir "$HOME/.cache/guategeeks-aws2026/build"
```

The Windows virtual environment is separate and must not be activated inside WSL.
