"""Remove only deployed source-IP restrictions, preserving the rest of the stack/code.

Run with --apply after inspecting the generated CloudFormation change set. Credentials
and signed code-download URLs are never written to reports or printed.
"""
import argparse
import ast
import base64
import copy
import hashlib
import io
import json
from pathlib import Path
import time
import urllib.request
import zipfile
import boto3

ACCOUNT = '590183968738'
STACK = 'guategeeks-aws2026'
REGION = 'us-east-1'
BUCKET = 'guategeeks-aws2026-artifacts-590183968738-us-east-1'
ROOT = Path(__file__).resolve().parents[2]
WORK = ROOT / 'Temp' / 'ApiOpenAccess'
REPORT = ROOT / 'backend' / 'deployment' / 'public-ip-access-verification.json'

def parse_policy(raw):
    if not raw:return {}
    try:return json.loads(raw)
    except json.JSONDecodeError:return json.loads(raw.replace('\\"','"'))

def patch_package(raw):
    result = io.BytesIO(); changed = []
    with zipfile.ZipFile(io.BytesIO(raw)) as source, zipfile.ZipFile(result, 'w', zipfile.ZIP_DEFLATED) as output:
        for item in source.infolist():
            content = source.read(item.filename)
            if item.filename in ('app.py','collaboration.py'):
                text = content.decode('utf-8'); tree = ast.parse(text)
                spans=[]
                for node in ast.walk(tree):
                    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name == 'authorize_source':
                        spans.append((node.lineno, node.end_lineno))
                    elif isinstance(node, ast.Expr) and isinstance(node.value, ast.Call):
                        call=node.value.func
                        if isinstance(call, ast.Name) and call.id == 'authorize_source' or isinstance(call,ast.Attribute) and call.attr == 'authorize_source':
                            spans.append((node.lineno,node.end_lineno))
                lines=text.splitlines(keepends=True)
                if spans:
                    excluded={line for start,end in spans for line in range(start,end+1)}
                    text=''.join(value for line,value in enumerate(lines,1) if line not in excluded)
                    compile(text,item.filename,'exec');content=text.encode('utf-8');changed.append(item.filename)
            output.writestr(item,content)
    return result.getvalue(),changed

def main():
    args=argparse.ArgumentParser();args.add_argument('--apply',action='store_true');args.add_argument('--prepare',action='store_true');opts=args.parse_args()
    assert not (opts.apply and opts.prepare)
    session=boto3.Session(profile_name='awsday',region_name=REGION)
    assert session.client('sts').get_caller_identity()['Account']==ACCOUNT,'Unexpected account'
    cfn=session.client('cloudformation');gateway=session.client('apigateway');lam=session.client('lambda')
    stack=cfn.describe_stacks(StackName=STACK)['Stacks'][0]
    assert stack['StackStatus'] in ('CREATE_COMPLETE','UPDATE_COMPLETE'),'Stack not ready'
    template=cfn.get_template(StackName=STACK,TemplateStage='Original')['TemplateBody']
    if isinstance(template,str):
        import yaml
        template=yaml.safe_load(template)
    template=copy.deepcopy(template)
    outputs={v['OutputKey']:v['OutputValue'] for v in stack['Outputs']}
    api_id=outputs['ApiUrl'].split('//',1)[1].split('.',1)[0]
    policy=parse_policy(gateway.get_rest_api(restApiId=api_id).get('policy','{}'))
    function_ids=[key for key,value in template['Resources'].items() if value['Type'] in ('AWS::Serverless::Function','AWS::Lambda::Function') and 'ALLOWED_CIDR' in value.get('Properties',{}).get('Environment',{}).get('Variables',{})]
    inspection={'stack':STACK,'status':stack['StackStatus'],'apiId':api_id,'ipRestrictionPresent':'aws:SourceIp' in json.dumps(policy),'guardedFunctions':function_ids}
    print(json.dumps(inspection),flush=True)
    if not (opts.apply or opts.prepare):return
    WORK.mkdir(parents=True,exist_ok=True)
    (WORK/'original-template.json').write_text(json.dumps(template,indent=2),encoding='utf-8')
    for key,value in template['Resources'].items():
        if value['Type'] not in ('AWS::Serverless::Api','AWS::ApiGateway::RestApi'):continue
        statements=value.get('Properties',{}).get('Policy',{}).get('Statement',[])
        value['Properties']['Policy']['Statement']=[s for s in statements if not (s.get('Effect')=='Deny' and 'aws:SourceIp' in json.dumps(s.get('Condition',{})))]
    expected={}
    for key in function_ids:
        function=cfn.describe_stack_resource(StackName=STACK,LogicalResourceId=key)['StackResourceDetail']['PhysicalResourceId']
        code=lam.get_function(FunctionName=function)
        with urllib.request.urlopen(code['Code']['Location'],timeout=60) as response:raw=response.read()
        (WORK/(key+'-original.zip')).write_bytes(raw)
        patched,changed=patch_package(raw);assert changed,'No source guard found'
        digest=hashlib.sha256(patched).hexdigest();object_key='public-ip-access/'+digest+'.zip'
        (WORK/(key+'-prepared.zip')).write_bytes(patched)
        if opts.apply:session.client('s3').put_object(Bucket=BUCKET,Key=object_key,Body=patched,ServerSideEncryption='AES256')
        properties=template['Resources'][key]['Properties']
        properties['Environment']['Variables'].pop('ALLOWED_CIDR')
        if template['Resources'][key]['Type']=='AWS::Serverless::Function':properties['CodeUri']={'Bucket':BUCKET,'Key':object_key}
        else:properties['Code']={'S3Bucket':BUCKET,'S3Key':object_key}
        expected[function]={'codeSha256':base64.b64encode(hashlib.sha256(patched).digest()).decode(),'changedModules':changed}
    template.get('Parameters',{}).pop('AllowedCidr',None)
    assert 'AllowedCidr' not in json.dumps(template) and 'aws:SourceIp' not in json.dumps(template)
    parameters=[{'ParameterKey':v['ParameterKey'],'UsePreviousValue':True} for v in stack.get('Parameters',[]) if v['ParameterKey'] in template.get('Parameters',{})]
    (WORK/'prepared-template.json').write_text(json.dumps(template,indent=2),encoding='utf-8')
    if opts.prepare:
        print(json.dumps({'prepared':True,'cloudModified':False,'changedFunctions':list(expected),'removedParameter':'AllowedCidr','otherParametersPreserved':True}),flush=True)
        return
    name='open-source-ip-'+str(int(time.time()))
    body=json.dumps(template)
    assert len(body.encode())<=51200,'Template exceeds direct change-set size'
    cfn.create_change_set(StackName=STACK,ChangeSetName=name,ChangeSetType='UPDATE',TemplateBody=body,Parameters=parameters,Capabilities=['CAPABILITY_IAM','CAPABILITY_AUTO_EXPAND'],Description='Allow authenticated clients from any source IP; preserve existing feature set')
    cfn.get_waiter('change_set_create_complete').wait(StackName=STACK,ChangeSetName=name,WaiterConfig={'Delay':5,'MaxAttempts':60})
    changes=cfn.describe_change_set(StackName=STACK,ChangeSetName=name)
    summary=[dict(logicalId=v['ResourceChange']['LogicalResourceId'],action=v['ResourceChange']['Action'],replacement=v['ResourceChange'].get('Replacement')) for v in changes.get('Changes',[])]
    print(json.dumps({'changeSet':name,'changes':summary}),flush=True)
    assert all((v['logicalId'] in function_ids or v['logicalId'].startswith('ControlApi')) and v['replacement']!='True' for v in summary),'Unexpected change-set scope; not executed'
    (WORK/'change-set.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
    assert any(v['logicalId'].startswith('ControlApiDeployment') and v['action']=='Add' for v in summary), 'REST policy must be included in a fresh stage deployment'
    cfn.execute_change_set(StackName=STACK,ChangeSetName=name)
    cfn.get_waiter('stack_update_complete').wait(StackName=STACK,WaiterConfig={'Delay':5,'MaxAttempts':120})

    after=cfn.describe_stacks(StackName=STACK)['Stacks'][0]
    applied=parse_policy(gateway.get_rest_api(restApiId=api_id).get('policy','{}'))
    assert 'aws:SourceIp' not in json.dumps(applied)
    assert {v['ParameterKey']:v['ParameterValue'] for v in after['Parameters']}=={v['ParameterKey']:v['ParameterValue'] for v in stack['Parameters'] if v['ParameterKey']!='AllowedCidr'}
    for function,record in expected.items():
        cfg=lam.get_function_configuration(FunctionName=function)
        assert cfg['CodeSha256']==record['codeSha256']
        assert 'ALLOWED_CIDR' not in cfg['Environment']['Variables']
    secret_arn=next(v['OutputValue'] for v in after['Outputs'] if v['OutputKey']=='AuthSecretArn')
    credentials=json.loads(session.client('secretsmanager').get_secret_value(SecretId=secret_arn)['SecretString'])
    auth='Basic '+base64.b64encode((credentials['username']+':'+credentials['password']).encode()).decode()
    def check(headers):
        request=urllib.request.Request(outputs['ApiUrl']+'/v1/session',headers=headers)
        try:
            with urllib.request.urlopen(request,timeout=20) as response:return response.status,json.loads(response.read())
        except urllib.error.HTTPError as error:return error.code,json.loads(error.read())
    valid,data=check({'Authorization':auth});invalid,_=check({})
    assert valid==200 and data['connected'] and invalid==401
    report=dict(inspection,stackStatus=after['StackStatus'],sourceRestrictionRemoved=True,authenticationRequired=True,authenticatedStatus=valid,unauthenticatedStatus=invalid,parametersPreserved=True,functions=expected,changes=summary)
    REPORT.write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'stackStatus':after['StackStatus'],'authenticatedStatus':valid,'unauthenticatedStatus':invalid,'sourceRestrictionRemoved':True}),flush=True)

if __name__=='__main__':main()
