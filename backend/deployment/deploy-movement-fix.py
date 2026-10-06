"""Prepare/apply a code-only movement patch; preserve all other deployed files.

Default is a read-only plan. --apply updates the two existing room functions
using RevisionId guards. Downloaded rollback packages stay in ignored Temp.
"""
import argparse
import hashlib
import io
import json
import pathlib
import urllib.request
import zipfile
import boto3
from botocore.exceptions import ClientError

ROOT = pathlib.Path(__file__).resolve().parents[1]


def session():
    paths = sorted((pathlib.Path.home() / '.aws/login/cache').glob('*.json'), key=lambda p: p.stat().st_mtime, reverse=True)
    for path in paths:
        token = json.loads(path.read_text()).get('accessToken', {})
        if token.get('accountId') != '590183968738':
            continue
        result = boto3.Session(aws_access_key_id=token['accessKeyId'], aws_secret_access_key=token['secretAccessKey'],
                               aws_session_token=token['sessionToken'], region_name='us-east-1')
        try:
            assert result.client('sts').get_caller_identity()['Account'] == '590183968738'
            return result
        except ClientError as error:
            if error.response['Error']['Code'] not in ('ExpiredToken', 'InvalidClientTokenId'):
                raise
    raise RuntimeError('Renew AWS login for profile awsday-login before deployment.')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    aws_session = session()
    cloud = aws_session.client('cloudformation')
    client = aws_session.client('lambda')
    resources = cloud.describe_stack_resources(StackName='guategeeks-aws2026')['StackResources']
    names = {r['LogicalResourceId']: r['PhysicalResourceId'] for r in resources}
    source = (ROOT / 'src/collaboration.py').read_bytes()
    backup = ROOT.parent / 'Temp/MultiuserRollback'
    backup.mkdir(parents=True, exist_ok=True)
    report = dict(applied=False, sourceSha256=hashlib.sha256(source).hexdigest(), functions=[])
    packages = []
    for logical in ('ControlFunction', 'CollabFunction'):
        name = names[logical]
        current = client.get_function(FunctionName=name)
        with urllib.request.urlopen(current['Code']['Location'], timeout=30) as response:
            original = response.read()
        (backup / (logical + '.zip')).write_bytes(original)
        output = io.BytesIO()
        with zipfile.ZipFile(io.BytesIO(original)) as old, zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as new:
            assert 'collaboration.py' in old.namelist()
            previous = old.read('collaboration.py')
            for entry in old.infolist():
                new.writestr(entry, source if entry.filename == 'collaboration.py' else old.read(entry.filename))
        packages.append((name, current['Configuration']['RevisionId'], output.getvalue()))
        report['functions'].append(dict(name=name, previousSourceSha256=hashlib.sha256(previous).hexdigest(),
                                        changedFiles=['collaboration.py']))
    if args.apply:
        for name, revision, package in packages:
            client.update_function_code(FunctionName=name, ZipFile=package, RevisionId=revision)
            client.get_waiter('function_updated_v2').wait(FunctionName=name, WaiterConfig=dict(Delay=2, MaxAttempts=30))
            current = client.get_function(FunctionName=name)
            with urllib.request.urlopen(current['Code']['Location'], timeout=30) as response:
                deployed = response.read()
            with zipfile.ZipFile(io.BytesIO(deployed)) as archive:
                assert archive.read('collaboration.py') == source
        report['applied'] = True
    (ROOT / 'deployment/movement-backend-update.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report))


if __name__ == '__main__':
    main()
