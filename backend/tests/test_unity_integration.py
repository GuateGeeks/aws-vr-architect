import json
from pathlib import Path
import sys
import unittest
import contextlib
import io
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'tools'))
from connect_quest import connection
from export_unity_contract import export
import connect_quest


class UnityIntegrationTests(unittest.TestCase):
    def test_creation_and_status_preserve_design_identity(self):
        fixtures = export()
        fingerprint = json.loads(fixtures['validation']['Json'])['graphHash']
        for key in ('created', 'pending', 'ready', 'rollback'):
            self.assertEqual(fingerprint, json.loads(fixtures[key]['Json'])['graphHash'])
        self.assertFalse(json.loads(fixtures['created']['Json'])['success'])
        self.assertTrue(json.loads(fixtures['ready']['Json'])['success'])
        self.assertFalse(json.loads(fixtures['rollback']['Json'])['success'])
        self.assertEqual(202, fixtures['accepted']['Code'])

    def test_bootstrap_expires_and_contains_no_aws_credentials(self):
        data = connection({'ApiUrl': 'https://example.invalid/prod/'}, {'username': 'quest-demo', 'password': 'test-only'}, '2', 100)
        self.assertEqual(160, data['expiresAt'])
        self.assertEqual('2', data['deploymentId'])
        self.assertEqual('https://example.invalid/prod', data['endpoint'])
        self.assertEqual({'endpoint', 'username', 'password', 'deploymentId', 'expiresAt'}, set(data))

    def test_bootstrap_rejects_unsafe_urls_and_slots(self):
        for url in ('http://example.invalid', 'https://user:password@example.invalid', 'https://example.invalid?secret=x', 'https://example.invalid#x'):
            with self.assertRaises(ValueError): connection({'ApiUrl': url}, {'username': 'u', 'password': 'p'}, '1', 0)
        with self.assertRaises(ValueError): connection({'ApiUrl': 'https://example.invalid'}, {'username': 'u', 'password': 'p'}, '4', 0)

    def test_usb_bootstrap_uses_stdin_and_cleans_up_after_consumption(self):
        client = MagicMock()
        client.describe_stacks.return_value = {'Stacks': [{'Outputs': [
            {'OutputKey': 'ApiUrl', 'OutputValue': 'https://example.invalid/prod'},
            {'OutputKey': 'AuthSecretArn', 'OutputValue': 'fixture-secret'}]}]}
        client.get_secret_value.return_value = {'SecretString': json.dumps({'username': 'quest-demo', 'password': 'fixture-password-never-log'})}
        commands, sent = [], []
        def run(command, **kwargs):
            commands.append(command)
            if kwargs.get('input'):
                sent.append(json.loads(kwargs['input']))
            if command[-1] == 'ro.product.model': return SimpleNamespace(stdout=b'Quest 3\n', returncode=0)
            return SimpleNamespace(stdout=b'', returncode=1 if command[-1].startswith('test -f ') else 0)
        output = io.StringIO()
        with patch.object(sys, 'argv', ['connect_quest.py', '--profile', 'test', '--serial', 'fixture']), \
             patch.object(connect_quest.boto3, 'Session') as session, \
             patch.object(connect_quest.subprocess, 'run', side_effect=run), contextlib.redirect_stdout(output):
            session.return_value.client.return_value = client
            connect_quest.main()
        self.assertEqual(1, len(sent))
        self.assertEqual('fixture-password-never-log', sent[0]['password'])
        self.assertNotIn('fixture-password-never-log', str(commands) + output.getvalue())
        self.assertTrue(commands[-1][-1].startswith('rm -f ' + connect_quest.SESSION_PATH))
        client.create_stack.assert_not_called()
