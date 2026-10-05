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
        self.assertEqual('gpt-realtime-2.1-mini',posted['session']['model'])
        self.assertEqual({'type':'semantic_vad','eagerness':'auto','create_response':False,'interrupt_response':False},posted['session']['audio']['input']['turn_detection'])
        self.assertEqual(3300,data['maxSessionSeconds'])
        self.assertEqual({'type':'retention_ratio','retention_ratio':0.8,'token_limits':{'post_instructions':8000}},posted['session']['truncation'])
        self.assertEqual(4096,posted['session']['max_output_tokens'], 'Code tool arguments must not be truncated by a tiny speech cap')
        names={tool['name'] for tool in posted['session']['tools']}
        self.assertEqual({'get_context','propose_architecture','propose_workflow','get_lambda_code','propose_lambda_code','lambda_code_action','diagnostics_action','highlight_node','inspect_last_event','open_deployment_review','component_action','connection_action','set_component_size','ui_action','spatial_action','read_component','slot_action','send_event'},names)
        client.update_item.assert_called_once()

    def test_quota_denies_before_secret_or_provider_access(self):
        client=MagicMock();client.update_item.side_effect=ClientError({'Error':{'Code':'ConditionalCheckFailedException'}},'UpdateItem')
        with patch.dict(os.environ,OPENAI_SECRET_ARN='secret-ai',AI_QUOTA_TABLE='quota'),patch.object(app,'client',return_value=client),patch('assistant.urllib.request.build_opener') as provider:
            result=app.handler(self.event('POST','/v1/assistant/session'),self.context)
        self.assertEqual(429,result['statusCode']);provider.assert_not_called();client.get_secret_value.assert_not_called()

    def test_guest_credentials_and_safety_identifiers_are_independent(self):
        client=MagicMock();client.get_secret_value.return_value={'SecretString':'sk-test-fixture-not-a-real-key'}
        replies=[]
        for token in ('ek_alice','ek_bob'):
            response=MagicMock();response.__enter__.return_value.read.return_value=json.dumps(dict(value=token,expires_at=9999999999)).encode();replies.append(response)
        opener=MagicMock();opener.open.side_effect=replies
        with patch.dict(os.environ,OPENAI_SECRET_ARN='secret-ai',AI_QUOTA_TABLE='quota'),patch.object(app,'client',return_value=client),patch('assistant.urllib.request.build_opener',return_value=opener):
            alice=assistant.create_session(app,'alice');bob=assistant.create_session(app,'bob')
        self.assertNotEqual(alice['clientSecret'],bob['clientSecret'])
        headers=[{k.lower():v for k,v in call.args[0].headers.items()} for call in opener.open.call_args_list]
        self.assertNotEqual(headers[0]['openai-safety-identifier'],headers[1]['openai-safety-identifier'])
        calls=client.update_item.call_args_list
        self.assertIn('alice',calls[0].kwargs['Key']['id']['S']);self.assertIn('bob',calls[2].kwargs['Key']['id']['S'])
        self.assertEqual({'N':'3'},calls[0].kwargs['ExpressionAttributeValues'][':limit'])
        self.assertEqual({'N':'12'},calls[1].kwargs['ExpressionAttributeValues'][':limit'])

    def test_guest_quota_denies_before_provider_or_secret_access(self):
        client=MagicMock();client.update_item.side_effect=ClientError({'Error':{'Code':'ConditionalCheckFailedException'}},'UpdateItem')
        with patch.dict(os.environ,OPENAI_SECRET_ARN='secret-ai',AI_QUOTA_TABLE='quota'),patch.object(app,'client',return_value=client),patch('assistant.urllib.request.build_opener') as provider:
            with self.assertRaises(app.ApiError) as caught: assistant.create_session(app,'alice')
        self.assertEqual('assistant_user_quota',caught.exception.code)
        client.get_secret_value.assert_not_called();provider.assert_not_called()

    def test_provider_failure_never_echoes_secret_or_body(self):
        client=MagicMock();client.get_secret_value.return_value={'SecretString':'sk-test-private-value'}
        opener=MagicMock();opener.open.side_effect=urllib.error.HTTPError('https://api.openai.com',401,'secret-provider-detail',{},io.BytesIO(b'sk-private'))
        with patch.dict(os.environ,OPENAI_SECRET_ARN='secret-ai',AI_QUOTA_TABLE='quota'),patch.object(app,'client',return_value=client),patch('assistant.urllib.request.build_opener',return_value=opener):
            result=app.handler(self.event('POST','/v1/assistant/session'),self.context)
        self.assertEqual(502,result['statusCode']);self.assertNotIn('sk-',result['body']);self.assertNotIn('secret-provider-detail',result['body'])
