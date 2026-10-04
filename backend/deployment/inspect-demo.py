"""Inventory only this backend's three demo slots; never enumerate unrelated workloads."""
import json
from pathlib import Path
from datetime import datetime, timezone
import boto3
from botocore.exceptions import ClientError

session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738', 'Unexpected AWS account'
cfn = session.client('cloudformation')
result = {'at': datetime.now(timezone.utc).isoformat(), 'region': 'us-east-1', 'stacks': []}
for name in ['guategeeks-aws2026'] + [f'ggawsday-demo-{i}' for i in (1, 2, 3)]:
    try:
        stack = cfn.describe_stacks(StackName=name)['Stacks'][0]
    except ClientError as error:
        if error.response['Error']['Code'] == 'ValidationError' and 'does not exist' in error.response['Error']['Message']:
            result['stacks'].append({'name': name, 'status': 'ABSENT'}); continue
        raise
    item = {'name': name, 'id': stack['StackId'], 'status': stack['StackStatus'], 'tags': stack.get('Tags', [])}
    if name.startswith('ggawsday-demo-'):
        item['resources'] = [r for p in cfn.get_paginator('list_stack_resources').paginate(StackName=name) for r in p['StackResourceSummaries']]
        item['failures'] = [{'type': e['ResourceType'], 'status': e['ResourceStatus'], 'reason': e.get('ResourceStatusReason', '')}
            for e in cfn.describe_stack_events(StackName=name)['StackEvents'] if e['ResourceStatus'].endswith('FAILED')][:10]
    result['stacks'].append(item)
result['logGroups'] = [g['logGroupName'] for p in session.client('logs').get_paginator('describe_log_groups').paginate(logGroupNamePrefix='/aws/lambda/ggawsday-demo-') for g in p['logGroups']]
Path('deployment/demo-inventory-latest.json').write_text(json.dumps(result, default=str, indent=2) + '\n')
print(json.dumps(result, default=str, indent=2))
