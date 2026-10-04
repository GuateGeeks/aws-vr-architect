"""Rotate Basic Auth credentials without writing plaintext to disk or stdout."""
import argparse
import json
import secrets
import boto3

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--profile', required=True)
parser.add_argument('--region', default='us-east-1')
parser.add_argument('--stack', default='guategeeks-aws2026')
args = parser.parse_args()
session = boto3.Session(profile_name=args.profile, region_name=args.region)
outputs = session.client('cloudformation').describe_stacks(StackName=args.stack)['Stacks'][0]['Outputs']
secret_arn = next(o['OutputValue'] for o in outputs if o['OutputKey'] == 'AuthSecretArn')
session.client('secretsmanager').put_secret_value(SecretId=secret_arn, SecretString=json.dumps({'username': 'quest-demo', 'password': secrets.token_urlsafe(40)}))
print('Password rotated. Retrieve securely from Secrets Manager; API cache expires within 60 seconds.')
