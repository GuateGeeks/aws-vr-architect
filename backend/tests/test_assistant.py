import io
import json
import os
import unittest
import urllib.error
from unittest.mock import MagicMock, patch
import test_backend as baseline
import app
import assistant
from botocore.exceptions import ClientError


class AssistantTests(unittest.TestCase):
    setUp = baseline.ApiTests.setUp
    event = baseline.ApiTests.event

    def test_authentication_and_missing_configuration_fail_closed(self):
        event = self.event('POST', '/v1/assistant/session')
        with patch.dict(os.environ, OPENAI_SECRET_ARN=''), patch.object(app, 'client') as client:
            self.assertEqual(503, app.handler(event, self.context)['statusCode'])
            client.assert_not_called()
        event['headers'] = {}
        with patch.object(assistant, 'create_session') as create:
            self.assertEqual(401, app.handler(event, self.context)['statusCode'])
            create.assert_not_called()

    def test_broker_returns_only_ephemeral_token_and_owns_tools(self):
        client = MagicMock(); client.get_secret_value.return_value = {'SecretString': '{"OPENAI_API_KEY":"sk-test-fixture-not-a-real-key"}'}
        response = MagicMock(); response.__enter__.return_value.read.return_value = b'{"value":"ek_test","expires_at":9999999999}'
        opener = MagicMock(); opener.open.return_value = response
        with patch.dict(os.environ, OPENAI_SECRET_ARN='secret-ai', AI_QUOTA_TABLE='quota'), patch.object(app,'client',return_value=client), patch('assistant.urllib.request.build_opener',return_value=opener):
            result = app.handler(self.event('POST','/v1/assistant/session',{'model':'malicious','tools':[{'name':'shell'}]}),self.context)
        self.assertEqual(200,result['statusCode']); data=json.loads(result['body'])
        self.assertEqual('ek_test',data['clientSecret']); self.assertNotIn('sk-test',result['body'])
        request=opener.open.call_args.args[0]; posted=json.loads(request.data)
        self.assertEqual(30,posted['expires_after']['seconds'])
        self.assertEqual('gpt-realtime-2.1',posted['session']['model'])
        self.assertEqual({'type':'semantic_vad','eagerness':'auto','create_response':False,'interrupt_response':False},posted['session']['audio']['input']['turn_detection'])
        self.assertEqual(3300,data['maxSessionSeconds'])
        names={tool['name'] for tool in posted['session']['tools']}
        self.assertEqual({'get_context','propose_architecture','propose_workflow','get_lambda_code','propose_lambda_code','lambda_code_action','diagnostics_action','highlight_node','inspect_last_event','open_deployment_review','component_action','connection_action','set_component_size','ui_action','spatial_action'},names)
        client.update_item.assert_called_once()

    def test_quota_denies_before_secret_or_provider_access(self):
        client=MagicMock();client.update_item.side_effect=ClientError({'Error':{'Code':'ConditionalCheckFailedException'}},'UpdateItem')
        with patch.dict(os.environ,OPENAI_SECRET_ARN='secret-ai',AI_QUOTA_TABLE='quota'),patch.object(app,'client',return_value=client),patch('assistant.urllib.request.build_opener') as provider:
            result=app.handler(self.event('POST','/v1/assistant/session'),self.context)
        self.assertEqual(429,result['statusCode']);provider.assert_not_called();client.get_secret_value.assert_not_called()

    def test_provider_failure_never_echoes_secret_or_body(self):
        client=MagicMock();client.get_secret_value.return_value={'SecretString':'sk-test-private-value'}
        opener=MagicMock();opener.open.side_effect=urllib.error.HTTPError('https://api.openai.com',401,'secret-provider-detail',{},io.BytesIO(b'sk-private'))
        with patch.dict(os.environ,OPENAI_SECRET_ARN='secret-ai',AI_QUOTA_TABLE='quota'),patch.object(app,'client',return_value=client),patch('assistant.urllib.request.build_opener',return_value=opener):
            result=app.handler(self.event('POST','/v1/assistant/session'),self.context)
        self.assertEqual(502,result['statusCode']);self.assertNotIn('sk-',result['body']);self.assertNotIn('secret-provider-detail',result['body'])
