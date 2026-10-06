"""Add bounded WebSocket event rooms to the existing SAM control plane."""
def configure(resources):
    ref = lambda name: {'Ref': name}
    att = lambda name: {'Fn::GetAtt': [name, 'Arn']}
    sub = lambda value: {'Fn::Sub': value}
    resources['CollabTable'] = {'Type': 'AWS::DynamoDB::Table', 'Properties': {
        'BillingMode': 'PAY_PER_REQUEST', 'AttributeDefinitions': [{'AttributeName': 'id', 'AttributeType': 'S'}],
        'KeySchema': [{'AttributeName': 'id', 'KeyType': 'HASH'}],
        'TimeToLiveSpecification': {'AttributeName': 'expiresAt', 'Enabled': True}, 'SSESpecification': {'SSEEnabled': True}}}
    resources['CollabApi'] = {'Type': 'AWS::ApiGatewayV2::Api', 'Properties': {'Name': sub('${DemoPrefix}-rooms'), 'ProtocolType': 'WEBSOCKET', 'RouteSelectionExpression': '$request.body.action'}}
    resources['CollabLogs'] = {'Type': 'AWS::Logs::LogGroup', 'Properties': {'LogGroupName': sub('/aws/lambda/${DemoPrefix}-rooms'), 'RetentionInDays': 7}}
    resources['CollabRole'] = {'Type': 'AWS::IAM::Role', 'Properties': {
        'AssumeRolePolicyDocument': {'Version': '2012-10-17', 'Statement': [{'Effect': 'Allow', 'Principal': {'Service': 'lambda.amazonaws.com'}, 'Action': 'sts:AssumeRole'}]},
        'Policies': [{'PolicyName': 'RoomAccess', 'PolicyDocument': {'Version': '2012-10-17', 'Statement': [
            {'Effect': 'Allow', 'Action': ['dynamodb:GetItem', 'dynamodb:PutItem'], 'Resource': att('CollabTable')},
            {'Effect': 'Allow', 'Action': ['execute-api:ManageConnections'], 'Resource': sub('arn:${AWS::Partition}:execute-api:${AWS::Region}:${AWS::AccountId}:${CollabApi}/rooms/POST/@connections/*')},
            {'Effect': 'Allow', 'Action': ['logs:CreateLogStream', 'logs:PutLogEvents'], 'Resource': att('CollabLogs')}]} }]}}
    resources['CollabFunction'] = {'Type': 'AWS::Serverless::Function', 'DependsOn': 'CollabLogs', 'Properties': {
        'FunctionName': sub('${DemoPrefix}-rooms'), 'Runtime': 'python3.13', 'Handler': 'collaboration.handler', 'CodeUri': 'src/',
        'MemorySize': 256, 'Timeout': 10, 'Role': att('CollabRole'),
        'Environment': {'Variables': {'COLLAB_TABLE': ref('CollabTable')}}}}
    resources['CollabIntegration'] = {'Type': 'AWS::ApiGatewayV2::Integration', 'Properties': {
        'ApiId': ref('CollabApi'), 'IntegrationType': 'AWS_PROXY',
        'IntegrationUri': sub('arn:${AWS::Partition}:apigateway:${AWS::Region}:lambda:path/2015-03-31/functions/${CollabFunction.Arn}/invocations')}}
    route_names = []
    for index, route in enumerate(['$connect', '$disconnect', '$default']):
        name = 'CollabRoute' + str(index)
        route_names.append(name)
        resources[name] = {'Type': 'AWS::ApiGatewayV2::Route', 'Properties': {'ApiId': ref('CollabApi'), 'RouteKey': route, 'AuthorizationType': 'NONE', 'Target': sub('integrations/${CollabIntegration}')}}
    # AutoDeploy avoids immutable deployment snapshots becoming stale on route changes.
    resources['CollabStage'] = {'Type': 'AWS::ApiGatewayV2::Stage', 'DependsOn': route_names, 'Properties': {
        'ApiId': ref('CollabApi'), 'StageName': 'rooms', 'AutoDeploy': True,
        'DefaultRouteSettings': {'ThrottlingBurstLimit': 100, 'ThrottlingRateLimit': 60, 'DataTraceEnabled': False}}}
    resources['CollabPermission'] = {'Type': 'AWS::Lambda::Permission', 'Properties': {'FunctionName': ref('CollabFunction'), 'Action': 'lambda:InvokeFunction', 'Principal': 'apigateway.amazonaws.com', 'SourceArn': sub('arn:${AWS::Partition}:execute-api:${AWS::Region}:${AWS::AccountId}:${CollabApi}/*')}}
    control = resources['ControlFunction']['Properties']['Environment']['Variables']
    control.update(COLLAB_TABLE=ref('CollabTable'), COLLAB_WS_URL=sub('wss://${CollabApi}.execute-api.${AWS::Region}.${AWS::URLSuffix}/rooms'))
    resources['ControlRole']['Properties']['Policies'][0]['PolicyDocument']['Statement'].append(
        {'Effect': 'Allow', 'Action': ['dynamodb:GetItem', 'dynamodb:PutItem'], 'Resource': att('CollabTable')})
