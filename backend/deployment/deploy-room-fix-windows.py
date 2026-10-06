"""Publish current room backend using an existing Windows AWS login session.

Build dependencies come from the previously validated SAM build; all application
sources are replaced with current source. No credentials are saved or printed.
"""
import hashlib
import io
import json
import pathlib
import time
import zipfile
import boto3

ROOT = pathlib.Path(__file__).resolve().parents[1]

def session():
    for path in (pathlib.Path.home()/'.aws/login/cache').glob('*.json'):
        token = json.loads(path.read_text())['accessToken']
        if token.get('accountId') != '590183968738':
            continue
        result = boto3.Session(aws_access_key_id=token['accessKeyId'],
            aws_secret_access_key=token['secretAccessKey'],
            aws_session_token=token['sessionToken'], region_name='us-east-1')
        assert result.client('sts').get_caller_identity()['Account'] == '590183968738'
        return result
    raise RuntimeError('No matching active AWS login')

def main():
    s = session(); cfn = s.client('cloudformation'); s3 = s.client('s3')
    stack = cfn.describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
    assert stack['StackStatus'] in ('UPDATE_COMPLETE','CREATE_COMPLETE')
    template = json.loads((ROOT/'template.json').read_text())
    bucket = 'guategeeks-aws2026-artifacts-590183968738-us-east-1'
    dependencies = ROOT/'.aws-sam/build/ControlFunction'
    assert (dependencies/'boto3/__init__.py').is_file()
    package = io.BytesIO()
    with zipfile.ZipFile(package,'w',zipfile.ZIP_DEFLATED) as z:
        for path in dependencies.rglob('*'):
            relative = path.relative_to(dependencies)
            if path.is_file() and len(relative.parts)>1 and '__pycache__' not in relative.parts and path.suffix != '.pyc':
                z.write(path,relative.as_posix())
        for path in (ROOT/'src').rglob('*'):
            if path.is_file() and '__pycache__' not in path.parts and path.suffix != '.pyc':
                z.write(path,path.relative_to(ROOT/'src').as_posix())
    raw=package.getvalue(); key='room-fix/'+hashlib.sha256(raw).hexdigest()+'.zip'
    s3.put_object(Bucket=bucket,Key=key,Body=raw)
    for resource in template['Resources'].values():
        if resource['Type']=='AWS::Serverless::Function':
            resource['Properties']['CodeUri']={'Bucket':bucket,'Key':key}
    name='room-fix-'+str(int(time.time()))
    params=[{'ParameterKey':p['ParameterKey'],'UsePreviousValue':True} for p in stack['Parameters'] if p['ParameterKey'] in template['Parameters']]
    cfn.create_change_set(StackName='guategeeks-aws2026',ChangeSetName=name,
        TemplateBody=json.dumps(template),Parameters=params,Capabilities=['CAPABILITY_IAM'],ChangeSetType='UPDATE')
    cfn.get_waiter('change_set_create_complete').wait(StackName='guategeeks-aws2026',ChangeSetName=name)
    changes=cfn.describe_change_set(StackName='guategeeks-aws2026',ChangeSetName=name)
    summary=[{k:x['ResourceChange'].get(k) for k in ('Action','LogicalResourceId','Replacement')} for x in changes['Changes']]
    assert all(x['Replacement'] not in ('True','Conditional') for x in summary),summary
    print(json.dumps({'changes':summary}),flush=True)
    cfn.execute_change_set(StackName='guategeeks-aws2026',ChangeSetName=name)
    cfn.get_waiter('stack_update_complete').wait(StackName='guategeeks-aws2026',WaiterConfig={'Delay':10,'MaxAttempts':90})
    print('UPDATE_COMPLETE',flush=True)

if __name__=='__main__':main()
