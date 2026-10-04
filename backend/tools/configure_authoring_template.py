"""Code authoring resources shared by the reproducible template builder."""

def configure(resources):
    resources['CodeTestLogs'] = {'Type': 'AWS::Logs::LogGroup', 'Properties': {
        'LogGroupName': {'Fn::Sub': '/aws/lambda/${DemoPrefix}-code-test'}, 'RetentionInDays': 7}}
    resources['CodeTestRole'] = {'Type': 'AWS::IAM::Role', 'Properties': {
        'AssumeRolePolicyDocument': {'Version': '2012-10-17', 'Statement': [{'Effect': 'Allow', 'Principal': {'Service': 'lambda.amazonaws.com'}, 'Action': 'sts:AssumeRole'}]},
        'Policies': [{'PolicyName': 'OwnTestLogsOnly', 'PolicyDocument': {'Version': '2012-10-17', 'Statement': [
            {'Effect': 'Allow', 'Action': ['logs:CreateLogStream', 'logs:PutLogEvents'], 'Resource': {'Fn::Sub': 'arn:${AWS::Partition}:logs:${AWS::Region}:${AWS::AccountId}:log-group:/aws/lambda/${DemoPrefix}-code-test:*'}}]}}]}}
    resources['CodeTestFunction'] = {'Type': 'AWS::Serverless::Function', 'DependsOn': ['CodeTestLogs'], 'Properties': {
        'FunctionName': {'Fn::Sub': '${DemoPrefix}-code-test'}, 'Runtime': 'python3.13', 'Handler': 'code_runner.handler',
        'CodeUri': 'src/', 'MemorySize': 128, 'Timeout': 3, 'Role': {'Fn::GetAtt': ['CodeTestRole', 'Arn']}}}
    statements = resources['ControlRole']['Properties']['Policies'][0]['PolicyDocument']['Statement']
    for sid in ['CodeAuthoring', 'IsolatedCodeTests']:
        statements[:] = [s for s in statements if s.get('Sid') != sid]
    statements.extend([
        {'Sid': 'CodeAuthoring', 'Effect': 'Allow', 'Action': ['lambda:GetFunction', 'lambda:GetFunctionConfiguration', 'lambda:ListVersionsByFunction', 'lambda:PublishVersion', 'lambda:UpdateFunctionCode'],
         'Resource': {'Fn::Sub': 'arn:${AWS::Partition}:lambda:${AWS::Region}:${AWS::AccountId}:function:${DemoPrefix}-demo-*'}},
        {'Sid': 'IsolatedCodeTests', 'Effect': 'Allow', 'Action': ['lambda:InvokeFunction'], 'Resource': {'Fn::GetAtt': ['CodeTestFunction', 'Arn']}}
    ])
    resources['ControlFunction']['Properties']['Environment']['Variables']['CODE_TEST_FUNCTION'] = {'Ref': 'CodeTestFunction'}
