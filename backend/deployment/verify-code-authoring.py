"""Exercise the reviewed code API on a newly-created, disposable demo slot.
Never modifies or deletes an occupied slot. Credentials remain in memory.
"""
import base64
import json
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
import boto3

session=boto3.Session(profile_name='awsday',region_name='us-east-1')
assert session.client('sts').get_caller_identity()['Account']=='590183968738'
cfn=session.client('cloudformation')
backend=cfn.describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
assert backend['StackStatus']=='UPDATE_COMPLETE'
outputs={v['OutputKey']:v['OutputValue'] for v in backend['Outputs']}
secret=json.loads(session.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
auth='Basic '+base64.b64encode((secret['username']+':'+secret['password']).encode()).decode()

def request(method,path,data=None,expected=(200,202)):
    req=urllib.request.Request(outputs['ApiUrl']+path,method=method,data=None if data is None else json.dumps(data).encode(),headers={'Authorization':auth,'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=45) as response:status=response.status;body=json.loads(response.read())
    except urllib.error.HTTPError as error:
        status=error.code;body=json.loads(error.read())
    if status not in expected:raise RuntimeError('Unexpected HTTP '+str(status)+' on '+method+' '+path.split('?')[0]+': '+str(body.get('error',''))+' '+str(body.get('message','')))
    return status,body

report={'startedUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime()),'checks':[]}
def passed(message):report['checks'].append(message);print('PASS '+message,flush=True)
source='def handler(event, context):\n    return {"total": event.get("amount", 0) * 2}\n'
assert request('POST','/v1/code/validate',{'source':source})[1]['valid'];passed('Authenticated syntax validation')
result=request('POST','/v1/code/test',{'source':source,'eventJson':'{"amount":21}'})[1]
assert result['passed'] and json.loads(result['output'])=={'total':42};passed('Dedicated Lambda draft test returns 42')
result=request('POST','/v1/code/test',{'source':'def handler(event, context):\n    while True: pass\n','eventJson':'{}'})[1]
assert not result['passed'];passed('Three-second draft timeout reported as failed')

slot=None;owned_stack=None;cleaned=False
for candidate in ('3','2','1'):
    status,_=request('GET','/v1/deployments/'+candidate,expected=(200,404))
    if status==404:slot=candidate;break
if not slot:raise SystemExit('No empty slot: isolated tests passed; publication test needs an empty slot. Existing resources were left untouched.')
graph={'schemaVersion':1,'region':'us-east-1','nodes':[{'id':'codecheckfn','name':'ATLAS disposable code check','kind':1,'setting':0},{'id':'codechecktable','name':'ATLAS disposable table','kind':2,'setting':0}],'links':[{'from':'codecheckfn','to':'codechecktable'}]}
try:
    _,created=request('POST','/v1/deployments',{'deploymentId':slot,'architecture':graph})
    owned_stack=created['stackId'];report['slot']=slot;report['stackId']=owned_stack
    for _ in range(100):
        _,state=request('GET','/v1/deployments/'+slot)
        assert state['stackId']==owned_stack
        if state['status']=='CREATE_COMPLETE':break
        if state.get('finished'):raise RuntimeError('Fixture creation failed: '+state['status'])
        time.sleep(3)
    else:raise RuntimeError('Fixture creation timed out')
    passed('Created own disposable Lambda/table in previously empty slot '+slot)
    code_path='/v1/deployments/'+slot+'/code'
    identity={'stackId':owned_stack,'resourceId':'codecheckfn'}
    read_path=code_path+'?'+urllib.parse.urlencode(identity)
    _,original=request('GET',read_path);assert 'def handler' in original['source']
    edited=original['source']+'\n# ATLAS reviewed code integration check\n'
    _,published=request('POST',code_path+'/publish',dict(identity,source=edited,revisionId=original['revisionId'],confirmed=True))
    checkpoint=published['rollbackVersion'];assert checkpoint.isdigit()
    def wait_code(expected):
        for _ in range(30):
            time.sleep(2);_,code=request('GET',read_path)
            if code['updateStatus']=='Successful':
                assert code['source']==expected
                return code
            assert code['updateStatus']!='Failed'
        raise RuntimeError('Code update not confirmed')
    updated=wait_code(edited);passed('Reviewed publication became active and preserved immutable checkpoint')
    request('POST',code_path+'/publish',dict(identity,source=edited,revisionId=original['revisionId'],confirmed=True),expected=(409,))
    passed('Stale revision refused without overwriting current code')
    request('POST',code_path+'/rollback',dict(identity,version=checkpoint,revisionId=updated['revisionId'],confirmed=True))
    restored=wait_code(original['source']);assert restored['version']=='$LATEST'
    passed('Reviewed code restoration recovered the exact original source')
finally:
    if owned_stack:
        _,current=request('GET','/v1/deployments/'+slot)
        if current['stackId']!=owned_stack:raise RuntimeError('Slot replaced: cleanup refused')
        cleanup_path='/v1/deployments/'+slot+'?'+urllib.parse.urlencode({'purge':'true','stackId':owned_stack})
        request('DELETE',cleanup_path)
        for _ in range(100):
            time.sleep(3);status,current=request('GET','/v1/deployments/'+slot,expected=(200,404))
            if status==404:cleaned=True;passed('Disposable fixture deleted; slot empty');break
            assert current['stackId']==owned_stack
        report['cleaned']=cleaned
        Path('deployment/code-authoring-acceptance.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
        if not cleaned:raise RuntimeError('Fixture cleanup not yet confirmed')
print('All code authoring live checks passed.',flush=True)
