import boto3,json,urllib.request,urllib.error,base64
s=boto3.Session(profile_name='awsday',region_name='us-east-1')
assert s.client('sts').get_caller_identity()['Account']=='590183968738'
st=s.client('cloudformation').describe_stacks(StackName='guategeeks-aws2026')['Stacks'][0]
o={v['OutputKey']:v['OutputValue'] for v in st['Outputs']}
cfg=s.client('lambda').get_function_configuration(FunctionName='ggawsday-control')
secret=json.loads(s.client('secretsmanager').get_secret_value(SecretId=o['AuthSecretArn'])['SecretString'])
auth='Basic '+base64.b64encode((secret['username']+':'+secret['password']).encode()).decode()
r=urllib.request.Request(o['ApiUrl']+'/v1/collab/rooms',data=b'{}',headers={'Authorization':auth,'Content-Type':'application/json'})
try:
 with urllib.request.urlopen(r,timeout=20) as v:status=v.status;body=json.loads(v.read())
except urllib.error.HTTPError as v:status=v.code;body=json.loads(v.read())
print(json.dumps(dict(stackStatus=st['StackStatus'],roomRouteStatus=status,roomRouteError=body.get('error'),roomResourcesPresent='CollabWebSocketUrl' in o,roomConfigured='COLLAB_TABLE' in cfg['Environment']['Variables'],lambdaConcurrency=s.client('lambda').get_account_settings()['AccountLimit']['ConcurrentExecutions'])))
