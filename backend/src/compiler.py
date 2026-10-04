"""Translate the bounded graph into CloudFormation, including real edge wiring."""
import json
from pathlib import Path
from graph import logical


def compile_graph(graph, stack_name, account, role_arn, partition='aws'):
    region = graph['region']
    nodes = {n['id']: n for n in graph['nodes']}
    ids = {i: logical(i) for i in nodes}
    names = {i: stack_name + '-' + ids[i].lower() for i in nodes}
    for i, n in nodes.items():
        if n['kind'] == 3:
            names[i] += '-' + account
        if n['kind'] == 4 and n['setting'] == 1:
            names[i] += '.fifo'
    def arn(service, suffix):
        return f'arn:{partition}:{service}:{region}:{account}:{suffix}'
    def ref(i):
        return {'Ref': ids[i]}
    def att(i, attr):
        return {'Fn::GetAtt': [ids[i], attr]}
    def outgoing(i, kinds=None):
        return [e['to'] for e in graph['links'] if e['from'] == i and (kinds is None or nodes[e['to']]['kind'] in kinds)]
    resources = {}
    def add(key, kind, props, **extra):
        resources[key] = {'Type': 'AWS::' + kind, 'Properties': props, **extra}
    source = Path(__file__).with_name('workload.py').read_text(encoding='utf-8')
    for i, node in nodes.items():
        lid, name, kind, setting = ids[i], names[i], node['kind'], node['setting']
        if kind == 0:
            add(lid, 'ApiGatewayV2::Api', {'Name': name, 'ProtocolType': 'HTTP', 'DisableExecuteApiEndpoint': False})
            target = outgoing(i, {1})[0]
            add(lid + 'Integration', 'ApiGatewayV2::Integration', {'ApiId': ref(i), 'IntegrationType': 'AWS_PROXY', 'IntegrationUri': att(target, 'Arn'), 'PayloadFormatVersion': '2.0', 'TimeoutInMillis': 10000})
            add(lid + 'Route', 'ApiGatewayV2::Route', {'ApiId': ref(i), 'RouteKey': 'POST /demo', 'AuthorizationType': 'AWS_IAM', 'Target': {'Fn::Join': ['/', ['integrations', {'Ref': lid + 'Integration'}]]}})
            add(lid + 'Stage', 'ApiGatewayV2::Stage', {'ApiId': ref(i), 'StageName': '$default', 'AutoDeploy': True, 'DefaultRouteSettings': {'ThrottlingBurstLimit': 5, 'ThrottlingRateLimit': 2}})
            add(lid + 'Permission', 'Lambda::Permission', {'Action': 'lambda:InvokeFunction', 'FunctionName': ref(target), 'Principal': 'apigateway.amazonaws.com', 'SourceAccount': account, 'SourceArn': {'Fn::Sub': f'arn:{partition}:execute-api:{region}:{account}:${{{lid}}}/*/POST/demo'}})
        elif kind == 1:
            retention = max([7] + [[7, 14, 30][nodes[t]['setting']] for t in outgoing(i, {6})])
            add(lid + 'Logs', 'Logs::LogGroup', {'LogGroupName': '/aws/lambda/' + name, 'RetentionInDays': retention})
            add(lid, 'Lambda::Function', {'FunctionName': name, 'Runtime': 'python3.13', 'Handler': 'index.handler', 'Role': role_arn,
                'MemorySize': [128, 256, 512, 1024][setting], 'Timeout': 10,
                'Code': {'ZipFile': source}, 'Environment': {'Variables': {'NODE_ID': i, 'TARGETS': json.dumps([{'kind': nodes[t]['kind'], 'name': names[t], 'nodeId': t} for t in outgoing(i, {2, 3, 4, 5})], separators=(',', ':'))}}}, DependsOn=[lid + 'Logs'])
        elif kind == 2:
            props = {'TableName': name, 'AttributeDefinitions': [{'AttributeName': 'id', 'AttributeType': 'S'}], 'KeySchema': [{'AttributeName': 'id', 'KeyType': 'HASH'}], 'BillingMode': 'PAY_PER_REQUEST' if setting == 0 else 'PROVISIONED', 'SSESpecification': {'SSEEnabled': True}}
            if setting == 1:
                props['ProvisionedThroughput'] = {'ReadCapacityUnits': 1, 'WriteCapacityUnits': 1}
            add(lid, 'DynamoDB::Table', props)
        elif kind == 3:
            props = {'BucketName': name, 'PublicAccessBlockConfiguration': {'BlockPublicAcls': True, 'BlockPublicPolicy': True, 'IgnorePublicAcls': True, 'RestrictPublicBuckets': True},
                'BucketEncryption': {'ServerSideEncryptionConfiguration': [{'ServerSideEncryptionByDefault': {'SSEAlgorithm': 'AES256'}}]},
                'OwnershipControls': {'Rules': [{'ObjectOwnership': 'BucketOwnerEnforced'}]},
                'VersioningConfiguration': {'Status': 'Enabled' if setting == 0 else 'Suspended'},
                'LifecycleConfiguration': {'Rules': [{'Id': 'DemoExpiry', 'Status': 'Enabled', 'ExpirationInDays': 1, 'NoncurrentVersionExpiration': {'NoncurrentDays': 1}, 'AbortIncompleteMultipartUpload': {'DaysAfterInitiation': 1}}]}}
            add(lid, 'S3::Bucket', props)
        elif kind == 4:
            props = {'QueueName': name, 'VisibilityTimeout': 60, 'MessageRetentionPeriod': 86400, 'SqsManagedSseEnabled': True}
            if setting:
                props.update(FifoQueue=True, ContentBasedDeduplication=True)
            add(lid, 'SQS::Queue', props)
        elif kind == 5:
            add(lid, 'Events::EventBus', {'Name': name})
        elif kind == 6:
            metrics = []
            specs = {0: ('AWS/ApiGateway', 'Count', 'ApiId'), 1: ('AWS/Lambda', 'Invocations', 'FunctionName'), 2: ('AWS/DynamoDB', 'ConsumedWriteCapacityUnits', 'TableName'), 3: ('AWS/S3', 'NumberOfObjects', 'BucketName'), 4: ('AWS/SQS', 'NumberOfMessagesSent', 'QueueName'), 5: ('AWS/Events', 'PutEventsApproximateSuccessCount', 'EventBusName')}
            substitutions = {}
            for e in graph['links']:
                if e['to'] != i:
                    continue
                t = e['from']
                ns, metric, dimension = specs[nodes[t]['kind']]
                value = names[t]
                if nodes[t]['kind'] == 0:
                    substitutions[ids[t]] = ref(t)
                    value = '${' + ids[t] + '}'
                metric_row = [ns, metric, dimension, value]
                if nodes[t]['kind'] == 3:
                    metric_row += ['StorageType', 'AllStorageTypes']
                metrics.append(metric_row)
            body = json.dumps({'widgets': [{'type': 'metric', 'x': 0, 'y': 0, 'width': 24, 'height': 8, 'properties': {'metrics': metrics, 'region': region, 'stat': 'Sum', 'period': 60, 'title': 'GuateGeeks AWS Day'}}]})
            add(lid, 'CloudWatch::Dashboard', {'DashboardName': name, 'DashboardBody': {'Fn::Sub': [body, substitutions]} if substitutions else body})
        resources[lid]['Metadata'] = {'NodeId': i, 'Kind': kind, 'Name': node['name']}
    # Group queue policies so multiple producers do not overwrite each other.
    policies = {}
    for edge in graph['links']:
        a, b = edge['from'], edge['to']
        ka, kb = nodes[a]['kind'], nodes[b]['kind']
        key = ids[a] + ids[b]
        if ka == 4 and kb == 1:
            add(key + 'Mapping', 'Lambda::EventSourceMapping', {'EventSourceArn': att(a, 'Arn'), 'FunctionName': ref(b), 'BatchSize': 1, 'Enabled': True})
        if ka == 3 and kb in (1, 4):
            bucket_arn = f'arn:{partition}:s3:::{names[a]}'
            if kb == 1:
                add(key + 'Permission', 'Lambda::Permission', {'Action': 'lambda:InvokeFunction', 'FunctionName': ref(b), 'Principal': 's3.amazonaws.com', 'SourceArn': bucket_arn, 'SourceAccount': account})
                resources[ids[a]]['DependsOn'] = [key + 'Permission']
                resources[ids[a]]['Properties']['NotificationConfiguration'] = {'LambdaConfigurations': [{'Event': 's3:ObjectCreated:*', 'Function': att(b, 'Arn')}]}
            else:
                policies.setdefault(b, []).append({'Effect': 'Allow', 'Principal': {'Service': 's3.amazonaws.com'}, 'Action': 'sqs:SendMessage', 'Resource': att(b, 'Arn'), 'Condition': {'ArnEquals': {'aws:SourceArn': bucket_arn}, 'StringEquals': {'aws:SourceAccount': account}}})
                resources[ids[a]]['DependsOn'] = [ids[b] + 'Policy']
                resources[ids[a]]['Properties']['NotificationConfiguration'] = {'QueueConfigurations': [{'Event': 's3:ObjectCreated:*', 'Queue': att(b, 'Arn')}]}
        if ka == 5 and kb in (1, 4):
            target = {'Id': ids[b], 'Arn': att(b, 'Arn')}
            if kb == 4 and nodes[b]['setting']:
                target['SqsParameters'] = {'MessageGroupId': 'aws-day'}
            rule_name = names[a] + '-' + ids[b][-8:].lower()
            add(key + 'Rule', 'Events::Rule', {'Name': rule_name, 'EventBusName': ref(a), 'EventPattern': {'source': ['guategeeks.demo']}, 'State': 'ENABLED', 'Targets': [target]})
            rule_arn = arn('events', 'rule/' + names[a] + '/' + rule_name)
            if kb == 1:
                add(key + 'Permission', 'Lambda::Permission', {'Action': 'lambda:InvokeFunction', 'FunctionName': ref(b), 'Principal': 'events.amazonaws.com', 'SourceArn': rule_arn, 'SourceAccount': account})
            else:
                policies.setdefault(b, []).append({'Effect': 'Allow', 'Principal': {'Service': 'events.amazonaws.com'}, 'Action': 'sqs:SendMessage', 'Resource': att(b, 'Arn'), 'Condition': {'ArnEquals': {'aws:SourceArn': rule_arn}, 'StringEquals': {'aws:SourceAccount': account}}})
    for i, statements in policies.items():
        add(ids[i] + 'Policy', 'SQS::QueuePolicy', {'Queues': [ref(i)], 'PolicyDocument': {'Version': '2012-10-17', 'Statement': statements}})
    return {'AWSTemplateFormatVersion': '2010-09-09', 'Description': 'GuateGeeks AWS Day bounded VR architecture', 'Resources': resources}
