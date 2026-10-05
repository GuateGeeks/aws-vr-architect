"""Generate the committed SAM template using only the Python standard library."""
import json
from pathlib import Path


def sub(value):
    return {'Fn::Sub': value}


def ref(value):
    return {'Ref': value}


def att(value, attr='Arn'):
    return {'Fn::GetAtt': [value, attr]}


def statement(actions, resource, **extra):
    return {'Effect': 'Allow', 'Action': actions, 'Resource': resource, **extra}


def role(service, statements):
    return {'Type': 'AWS::IAM::Role', 'Properties': {'AssumeRolePolicyDocument': {'Version': '2012-10-17', 'Statement': [{'Effect': 'Allow', 'Principal': {'Service': service}, 'Action': 'sts:AssumeRole'}]}, 'Policies': [{'PolicyName': 'DemoPermissions', 'PolicyDocument': {'Version': '2012-10-17', 'Statement': statements}}]}}


def build():
    prefix = '${DemoPrefix}-demo-'
    def arn(service, suffix):
        return sub('arn:${AWS::Partition}:' + service + ':${AWS::Region}:${AWS::AccountId}:' + suffix)
    functions = arn('lambda', 'function:' + prefix + '*')
    tables = arn('dynamodb', 'table/' + prefix + '*')
    queues = arn('sqs', prefix + '*')
    buckets = sub('arn:${AWS::Partition}:s3:::' + prefix + '*-${AWS::AccountId}')
    objects = sub('arn:${AWS::Partition}:s3:::' + prefix + '*-${AWS::AccountId}/*')
    buses = arn('events', 'event-bus/' + prefix + '*')
    rules = arn('events', 'rule/' + prefix + '*/*')
    logs = arn('logs', 'log-group:/aws/lambda/' + prefix + '*')
    stacks = arn('cloudformation', 'stack/' + prefix + '*/*')
    demo_data = [statement(['dynamodb:PutItem'], tables), statement(['s3:PutObject', 's3:GetObject'], objects),
                 statement(['sqs:GetQueueUrl', 'sqs:SendMessage', 'sqs:ReceiveMessage', 'sqs:DeleteMessage', 'sqs:GetQueueAttributes'], queues), statement(['events:PutEvents'], buses)]
    resources = {
        'AuthSecret': {'Type': 'AWS::SecretsManager::Secret', 'Properties': {'Description': 'GuateGeeks VR Basic Auth service account; rotate after the event', 'GenerateSecretString': {'SecretStringTemplate': '{"username":"quest-demo"}', 'GenerateStringKey': 'password', 'PasswordLength': 6, 'ExcludePunctuation': True}}},
        'WorkloadRole': role('lambda.amazonaws.com', demo_data + [statement(['logs:CreateLogStream', 'logs:PutLogEvents'], logs)]),
        'ProvisionerRole': role('cloudformation.amazonaws.com', [
            statement(['s3:*'], [buckets, objects]),
            statement(['dynamodb:CreateTable', 'dynamodb:DescribeTable', 'dynamodb:UpdateTable', 'dynamodb:DeleteTable', 'dynamodb:TagResource', 'dynamodb:UntagResource', 'dynamodb:ListTagsOfResource', 'dynamodb:DescribeContinuousBackups', 'dynamodb:DescribeTimeToLive'], tables),
            statement(['sqs:CreateQueue', 'sqs:DeleteQueue', 'sqs:GetQueueAttributes', 'sqs:SetQueueAttributes', 'sqs:GetQueueUrl', 'sqs:TagQueue', 'sqs:UntagQueue', 'sqs:ListQueueTags'], queues),
            statement(['lambda:CreateFunction', 'lambda:GetFunction', 'lambda:GetFunctionConfiguration', 'lambda:DeleteFunction', 'lambda:UpdateFunctionCode', 'lambda:UpdateFunctionConfiguration', 'lambda:AddPermission', 'lambda:RemovePermission', 'lambda:GetPolicy', 'lambda:TagResource', 'lambda:UntagResource', 'lambda:ListTags'], functions),
            statement(['lambda:CreateEventSourceMapping', 'lambda:UpdateEventSourceMapping', 'lambda:DeleteEventSourceMapping', 'lambda:GetEventSourceMapping', 'lambda:ListEventSourceMappings'], '*'),
            statement(['lambda:TagResource', 'lambda:UntagResource', 'lambda:ListTags'], arn('lambda', 'event-source-mapping:*')),
            statement(['events:CreateEventBus', 'events:DeleteEventBus', 'events:DescribeEventBus', 'events:PutRule', 'events:DeleteRule', 'events:DescribeRule', 'events:PutTargets', 'events:RemoveTargets', 'events:ListTargetsByRule', 'events:TagResource', 'events:UntagResource', 'events:ListTagsForResource'], [buses, rules]),
            statement(['logs:CreateLogGroup', 'logs:DeleteLogGroup', 'logs:PutRetentionPolicy', 'logs:DeleteRetentionPolicy', 'logs:TagResource', 'logs:UntagResource', 'logs:ListTagsForResource', 'logs:TagLogGroup', 'logs:UntagLogGroup', 'logs:ListTagsLogGroup'], logs),
            statement(['logs:DescribeLogGroups'], '*'),
            statement(['cloudwatch:PutDashboard', 'cloudwatch:GetDashboard', 'cloudwatch:DeleteDashboards'], sub('arn:${AWS::Partition}:cloudwatch::${AWS::AccountId}:dashboard/' + prefix + '*')),
            statement(['apigateway:GET', 'apigateway:POST', 'apigateway:PATCH', 'apigateway:PUT', 'apigateway:DELETE', 'apigateway:TagResource', 'apigateway:UntagResource'], [sub('arn:${AWS::Partition}:apigateway:${AWS::Region}::/apis'), sub('arn:${AWS::Partition}:apigateway:${AWS::Region}::/apis/*'), sub('arn:${AWS::Partition}:apigateway:${AWS::Region}::/tags/*')]),
            statement(['iam:PassRole'], att('WorkloadRole'), Condition={'StringEquals': {'iam:PassedToService': 'lambda.amazonaws.com'}})
        ]),
        'ControlRole': role('lambda.amazonaws.com', [
            statement(['secretsmanager:GetSecretValue'], ref('AuthSecret')),
            statement(['cloudformation:CreateStack', 'cloudformation:DeleteStack', 'cloudformation:DescribeStacks', 'cloudformation:ListStackResources', 'cloudformation:GetTemplate'], stacks),
            statement(['iam:PassRole'], att('ProvisionerRole'), Condition={'StringEquals': {'iam:PassedToService': 'cloudformation.amazonaws.com'}}),
            statement(['logs:CreateLogStream', 'logs:PutLogEvents'], att('ControlLogs')),
            statement(['logs:FilterLogEvents'], logs), statement(['dynamodb:Scan', 'dynamodb:GetItem'], tables),
            statement(['lambda:InvokeFunction'], functions), statement(['s3:PutObject', 's3:DeleteObject', 's3:DeleteObjectVersion'], objects),
            statement(['s3:ListBucket', 's3:ListBucketVersions'], buckets), statement(['sqs:SendMessage'], queues), statement(['events:PutEvents'], buses),
            statement(['execute-api:Invoke'], arn('execute-api', '*/$default/POST/demo'))
        ]),
        'ControlApi': {'Type': 'AWS::Serverless::Api', 'Properties': {
            'StageName': 'demo', 'EndpointConfiguration': 'REGIONAL', 'AlwaysDeploy': True,
            'Policy': {'Version': '2012-10-17', 'Statement': [
                {'Effect': 'Allow', 'Principal': '*', 'Action': 'execute-api:Invoke', 'Resource': 'execute-api:/*'}
            ]},
            'MethodSettings': [{'ResourcePath': '/*', 'HttpMethod': '*', 'ThrottlingBurstLimit': 10, 'ThrottlingRateLimit': 5, 'MetricsEnabled': True, 'DataTraceEnabled': False, 'LoggingLevel': 'OFF'}]
        }},
        'ControlLogs': {'Type': 'AWS::Logs::LogGroup', 'Properties': {'LogGroupName': sub('/aws/lambda/${DemoPrefix}-control'), 'RetentionInDays': 14}},
        'ControlFunction': {'Type': 'AWS::Serverless::Function', 'DependsOn': 'ControlLogs', 'Properties': {
            'FunctionName': sub('${DemoPrefix}-control'), 'Runtime': 'python3.13', 'Handler': 'app.handler', 'CodeUri': 'src/', 'MemorySize': 256, 'Timeout': 29,
            'Role': att('ControlRole'), 'Environment': {'Variables': {'DEMO_PREFIX': ref('DemoPrefix'), 'AUTH_SECRET_ARN': ref('AuthSecret'), 'PROVISIONER_ROLE_ARN': att('ProvisionerRole'), 'WORKLOAD_ROLE_ARN': att('WorkloadRole'), 'ACCOUNT_ID': ref('AWS::AccountId'), 'AWS_PARTITION': ref('AWS::Partition')}},
            'Events': {'Proxy': {'Type': 'Api', 'Properties': {'RestApiId': ref('ControlApi'), 'Path': '/{proxy+}', 'Method': 'ANY'}}}
        }},
        'ControlErrors': {'Type': 'AWS::CloudWatch::Alarm', 'Properties': {'AlarmDescription': 'Control API Lambda errors; inspect CloudWatch', 'Namespace': 'AWS/Lambda', 'MetricName': 'Errors', 'Dimensions': [{'Name': 'FunctionName', 'Value': ref('ControlFunction')}], 'Statistic': 'Sum', 'Period': 60, 'EvaluationPeriods': 1, 'Threshold': 1, 'ComparisonOperator': 'GreaterThanOrEqualToThreshold', 'TreatMissingData': 'notBreaching'}},
        'ApiServerErrors': {'Type': 'AWS::CloudWatch::Alarm', 'Properties': {'AlarmDescription': 'Control API 5xx, including handled AWS failures', 'Namespace': 'AWS/ApiGateway', 'MetricName': '5XXError', 'Dimensions': [{'Name': 'ApiName', 'Value': ref('AWS::StackName')}, {'Name': 'Stage', 'Value': 'demo'}], 'Statistic': 'Sum', 'Period': 60, 'EvaluationPeriods': 1, 'Threshold': 1, 'ComparisonOperator': 'GreaterThanOrEqualToThreshold', 'TreatMissingData': 'notBreaching'}},
        'DemoBudget': {'Type': 'AWS::Budgets::Budget', 'Condition': 'EnableBudget', 'Properties': {
            'Budget': {'BudgetName': sub('${DemoPrefix}-account-budget'), 'BudgetType': 'COST', 'TimeUnit': 'MONTHLY', 'BudgetLimit': {'Amount': ref('MonthlyBudgetUsd'), 'Unit': 'USD'}},
            'NotificationsWithSubscribers': [{'Notification': {'ComparisonOperator': 'GREATER_THAN', 'NotificationType': 'ACTUAL', 'Threshold': 80, 'ThresholdType': 'PERCENTAGE'}, 'Subscribers': [{'SubscriptionType': 'EMAIL', 'Address': ref('BudgetEmail')}]}]
        }}
    }
    # Explicit API name keeps CloudWatch dimensions deterministic.
    resources['ControlApi']['Properties']['Name'] = ref('AWS::StackName')
    resources['AiQuota'] = {'Type': 'AWS::DynamoDB::Table', 'Properties': {'BillingMode': 'PAY_PER_REQUEST',
        'AttributeDefinitions': [{'AttributeName': 'id', 'AttributeType': 'S'}], 'KeySchema': [{'AttributeName': 'id', 'KeyType': 'HASH'}],
        'TimeToLiveSpecification': {'AttributeName': 'expiresAt', 'Enabled': True}, 'SSESpecification': {'SSEEnabled': True}}}
    policies = resources['ControlRole']['Properties']['Policies'][0]['PolicyDocument']['Statement']
    policies.append(statement(['dynamodb:UpdateItem'], att('AiQuota')))
    policies.append({'Fn::If': ['EnableOpenAI', statement(['secretsmanager:GetSecretValue'], ref('OpenAISecretArn')), ref('AWS::NoValue')]})
    resources['ControlFunction']['Properties']['Environment']['Variables'].update(OPENAI_SECRET_ARN=ref('OpenAISecretArn'), OPENAI_REALTIME_MODEL=ref('OpenAIRealtimeModel'), AI_QUOTA_TABLE=ref('AiQuota'))
    from configure_authoring_template import configure
    configure(resources)
    from configure_collaboration_template import configure as configure_collaboration
    configure_collaboration(resources)
    return {
        'AWSTemplateFormatVersion': '2010-09-09', 'Transform': 'AWS::Serverless-2016-10-31', 'Description': 'GuateGeeks AWS Day VR control plane',
        'Parameters': {
            'OpenAISecretArn': {'Type': 'String', 'Default': '', 'Description': 'Optional existing Secrets Manager secret containing OPENAI_API_KEY; never put the key in a parameter'},
            'OpenAIRealtimeModel': {'Type': 'String', 'Default': 'gpt-realtime-2.1-mini', 'AllowedValues': ['gpt-realtime-2.1', 'gpt-realtime-2.1-mini']},
            'DemoPrefix': {'Type': 'String', 'Default': 'ggawsday', 'AllowedPattern': '[a-z][a-z0-9]{2,11}', 'Description': 'Unique prefix in this AWS account/region; do not change after deployment'},
            'BudgetEmail': {'Type': 'String', 'Default': '', 'Description': 'Optional email for a monthly account-wide budget alert'},
            'MonthlyBudgetUsd': {'Type': 'Number', 'Default': 10, 'MinValue': 1, 'Description': 'Alert threshold only, not a hard spending limit or cost estimate'}
        },
        'Conditions': {'EnableBudget': {'Fn::Not': [{'Fn::Equals': [ref('BudgetEmail'), '']}] }, 'EnableOpenAI': {'Fn::Not': [{'Fn::Equals': [ref('OpenAISecretArn'), '']}] }},
        'Resources': resources,
        'Outputs': {'ApiUrl': {'Value': sub('https://${ControlApi}.execute-api.${AWS::Region}.${AWS::URLSuffix}/demo')},
                    'CollabWebSocketUrl': {'Value': sub('wss://${CollabApi}.execute-api.${AWS::Region}.${AWS::URLSuffix}/rooms')},
                    'AuthSecretArn': {'Value': ref('AuthSecret')}, 'DemoPrefix': {'Value': ref('DemoPrefix')},
                    'WorkloadRoleArn': {'Value': att('WorkloadRole')}, 'ProvisionerRoleArn': {'Value': att('ProvisionerRole')}}
    }


if __name__ == '__main__':
    Path(__file__).resolve().parents[1].joinpath('template.json').write_text(json.dumps(build(), indent=2) + '\n', encoding='utf-8')
