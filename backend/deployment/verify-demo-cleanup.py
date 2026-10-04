"""Confirm absence of the exact workload resources recorded before authorized cleanup."""
import json
from pathlib import Path
from datetime import datetime, timezone
import boto3
from botocore.exceptions import ClientError

session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738'
before = json.loads(Path('deployment/demo-inventory-before-cleanup-2026-10-01.json').read_text())
checks = []
for stack in before['stacks']:
    if not stack['name'].startswith('ggawsday-demo-'):
        continue
    for item in stack.get('resources', []):
        kind, identifier = item['ResourceType'], item['PhysicalResourceId']
        try:
            if kind == 'AWS::ApiGatewayV2::Api':
                session.client('apigatewayv2').get_api(ApiId=identifier)
            elif kind == 'AWS::Lambda::Function':
                session.client('lambda').get_function(FunctionName=identifier)
            elif kind == 'AWS::DynamoDB::Table':
                session.client('dynamodb').describe_table(TableName=identifier)
            else:
                continue
        except ClientError as error:
            if error.response['Error']['Code'] not in ('NotFoundException', 'ResourceNotFoundException'):
                raise
            checks.append({'type': kind, 'id': identifier, 'status': 'ABSENT'})
        else:
            raise RuntimeError('Resource still exists: ' + identifier)
result = {'at': datetime.now(timezone.utc).isoformat(), 'checks': checks}
Path('deployment/demo-cleanup-verification.json').write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result, indent=2))
