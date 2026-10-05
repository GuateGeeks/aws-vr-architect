"""Deploy this VR backend source, retaining every existing stack parameter."""
import os
import json
import subprocess
import sys
from pathlib import Path
import boto3

root = Path(__file__).resolve().parents[1]
os.chdir(root)
os.environ['SAM_CLI_TELEMETRY'] = '0'
os.environ['AWS_PAGER'] = ''
session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738'
stack = session.client('cloudformation').describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
assert stack['StackStatus'] in ('UPDATE_COMPLETE', 'CREATE_COMPLETE')
template = json.loads(Path('template.json').read_text())
for value in stack['Parameters']:
    if value['ParameterKey'] in template['Parameters'] and not value['ParameterValue']:
        assert template['Parameters'][value['ParameterKey']].get('Default') == ''
# SAM rejects Key= with an empty value. Unspecified existing parameters use previous values;
# verify that their template defaults also remain empty before omitting them.
params = [v['ParameterKey'] + '=' + v['ParameterValue'] for v in stack['Parameters'] if v['ParameterValue'] and v['ParameterKey'] in template['Parameters']]
if '--skip-build' not in sys.argv:
    subprocess.run(['sam', 'build', '--template-file', 'template.json'], check=True)
subprocess.run(['sam', 'deploy', '--template-file', '.aws-sam/build/template.yaml',
    '--stack-name', 'guategeeks-aws2026', '--profile', 'awsday', '--region', 'us-east-1',
    '--s3-bucket', 'guategeeks-aws2026-artifacts-590183968738-us-east-1',
    '--capabilities', 'CAPABILITY_IAM', '--no-confirm-changeset', '--no-fail-on-empty-changeset',
    '--parameter-overrides', *params], check=True)
