"""Exercise live rooms with four independent sockets; never print credentials."""
import base64
import copy
import importlib.util
import json
import pathlib
import time
import io
import zipfile
import urllib.error
import urllib.parse
import urllib.request
from websockets.sync.client import connect

ROOT=pathlib.Path(__file__).resolve().parents[1]
spec=importlib.util.spec_from_file_location('room_deploy',ROOT/'deployment/deploy-room-fix-windows.py')
deploy=importlib.util.module_from_spec(spec);spec.loader.exec_module(deploy)

def main():
    s=deploy.session()
    stack=s.client('cloudformation').describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
    outputs={o['OutputKey']:o['OutputValue'] for o in stack['Outputs']}
    secret=json.loads(s.client('secretsmanager').get_secret_value(SecretId=outputs['AuthSecretArn'])['SecretString'])
    auth='Basic '+base64.b64encode((secret['username']+':'+secret['password']).encode()).decode()
    def http(path,body=None,authorized=True):
        headers={'Content-Type':'application/json'}
        if authorized:headers['Authorization']=auth
        request=urllib.request.Request(outputs['ApiUrl']+path,data=None if body is None else json.dumps(body).encode(),headers=headers)
        try:
            with urllib.request.urlopen(request,timeout=20) as response:return response.status,json.loads(response.read())
        except urllib.error.HTTPError as error:return error.code,json.loads(error.read())
    report={'stackStatus':stack['StackStatus']}
    assert stack['StackStatus']=='UPDATE_COMPLETE'
    status,_=http('/v1/session');assert status==200;report['authenticatedStatus']=status
    status,_=http('/v1/session',authorized=False);assert status==401;report['unauthenticatedStatus']=status
    graph=json.loads((ROOT/'examples/api-serverless.json').read_text())
    for i,node in enumerate(graph['nodes']):node['position']['x']=(i-1)*.7;node['viewScale']=0
    sockets=[];grants=[]
    def receive(ws,predicate):
        deadline=time.monotonic()+15
        while time.monotonic()<deadline:
            message=json.loads(ws.recv(timeout=max(.1,deadline-time.monotonic())))
            if predicate(message):return message
        raise AssertionError('Expected room message not received')
    try:
        for i in range(4):
            body={'name':'Room check '+str(i),'roomId':grants[0]['roomId'] if grants else '', 'graph':graph}
            status,grant=http('/v1/collab/rooms',body);assert status==201,(status,grant.get('error'),grant.get('message'))
            grants.append(grant)
            status,ticket=http('/v1/collab/ticket',{'token':grant['token']});assert status==200,(status,ticket.get('error'))
            ws=connect(ticket['websocketUrl']+'?ticket='+urllib.parse.quote(ticket['ticket']),open_timeout=15,legacy=True)
            sockets.append(ws);ws.send(json.dumps({'action':'heartbeat'}))
            receive(ws,lambda m:m.get('type')=='snapshot')
            for previous in sockets[:-1]:previous.send(json.dumps({'action':'heartbeat'}))
        assert len({g['userId'] for g in grants})==4
        assert {g['station'] for g in grants}=={0,1,2,3}
        status,full=http('/v1/collab/rooms',{'roomId':grants[0]['roomId'],'name':'Fifth check'});assert status==409,(status,full.get('message'))
        report.update(createRoomStatus=201,participants=4,uniqueStations=4,fifthParticipantStatus=status)
        sockets[0].send(json.dumps({'action':'sync'}))
        snap=receive(sockets[0],lambda m:m.get('type')=='snapshot' and len(m['members'])==4)
        changed=copy.deepcopy(snap['graph']);changed['nodes'][0]['name']='Live room verified'
        sockets[0].send(json.dumps({'action':'op','requestId':'live-edit','baseRevision':0,'graph':changed,'leases':[],'global':False}))
        for ws in sockets:
            edit=receive(ws,lambda m:m.get('requestId')=='live-edit');assert edit['accepted'] and edit['revision']==1 and edit['graph']==changed
        sockets[1].send(json.dumps({'action':'op','requestId':'live-stale','baseRevision':0,'graph':changed,'leases':[],'global':False}))
        stale=receive(sockets[1],lambda m:m.get('requestId')=='live-stale');assert not stale['accepted'] and stale['revision']==1
        key=changed['nodes'][0]['id']
        sockets[0].send(json.dumps({'action':'claim','objectId':key,'requestId':'live-lock'}))
        lock=receive(sockets[0],lambda m:m.get('requestId')=='live-lock');assert lock['accepted']
        sockets[1].send(json.dumps({'action':'claim','objectId':key,'requestId':'live-conflict'}))
        conflict=receive(sockets[1],lambda m:m.get('requestId')=='live-conflict');assert not conflict['accepted']
        report.update(editBroadcastClients=4,revision=1,staleEditRejected=True,lockConflictRejected=True)
        config=s.client('lambda').get_function_configuration(FunctionName='ggawsday-control')
        assert 'ALLOWED_CIDR' not in config['Environment']['Variables']
        report.update(ipRestrictionRemoved=True,realtimeModel=config['Environment']['Variables'].get('OPENAI_REALTIME_MODEL'))
        assert report['realtimeModel']=='gpt-realtime-2.1'
        function=s.client('lambda').get_function(FunctionName='ggawsday-control')
        with urllib.request.urlopen(function['Code']['Location'],timeout=30) as response:package=response.read()
        with zipfile.ZipFile(io.BytesIO(package)) as archive:
            for source in (ROOT/'src').glob('*.py'):
                assert archive.read(source.name)==source.read_bytes(),source.name
        report['deployedSourceMatches']=True
        policy=s.client('apigateway').get_rest_api(restApiId=outputs['ApiUrl'].split('//')[1].split('.')[0]).get('policy','')
        assert 'aws:SourceIp' not in policy
        report['gatewayIpRestrictionRemoved']=True
    finally:
        for ws in sockets:ws.close()
    report['verifiedAtUtc']=time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())
    (ROOT/'deployment/room-backend-verification.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report))

if __name__=='__main__':main()
