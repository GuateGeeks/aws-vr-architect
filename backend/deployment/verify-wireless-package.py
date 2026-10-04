"""Inspect the final APK for the active credential without printing or persisting it."""
import base64
import json
from pathlib import Path
import zipfile
import boto3

session = boto3.Session(profile_name='awsday', region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account'] == '590183968738'
stack = session.client('cloudformation').describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
outputs = {item['OutputKey']: item['OutputValue'] for item in stack['Outputs']}
credential = json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
assert len(credential['password']) == 6
patterns = [credential['password'].encode(), credential['password'].encode('utf-16-le'), base64.b64encode((credential['username'] + ':' + credential['password']).encode())]
apk = Path('../GuateGeeksAWSVR/Builds/Quest3/GuateGeeksAWSVR-Quest3.apk')
with zipfile.ZipFile(apk) as archive:
    for entry in archive.infolist():
        data = archive.read(entry)
        if any(pattern in data for pattern in patterns):
            raise RuntimeError('Active credential pattern found in APK; inspect privately before release')
print('PASS: active six-character credential and Basic value absent from unpacked APK contents.')
