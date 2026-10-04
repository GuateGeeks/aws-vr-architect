"""Synthesize a generic, non-user speech fixture. Credentials stay in memory."""
import array
import json
import urllib.request
import wave
import sys
from pathlib import Path
import boto3

session=boto3.Session(profile_name='awsday',region_name='us-east-1')
raw=session.client('secretsmanager').get_secret_value(SecretId='ggawsday-openai-realtime')['SecretString']
key=json.loads(raw)['OPENAI_API_KEY']
correction='--correction' in sys.argv
request=urllib.request.Request('https://api.openai.com/v1/audio/speech',method='POST',
    headers={'Authorization':'Bearer '+key,'Content-Type':'application/json'},
    data=json.dumps({'model':'gpt-4o-mini-tts','voice':'alloy','response_format':'pcm',
        'input':('Cambia el tamaño visual de todos los componentes al cincuenta por ciento, eh, mejor al setenta y cinco por ciento. No los reordenes.' if correction else 'Read the current context. Then say one short sentence describing whether the design is empty. Do not propose changes.')}).encode())
with urllib.request.urlopen(request,timeout=45) as response:
    samples=array.array('h',response.read())
# PCM output is 24 kHz signed 16-bit mono; linear interpolation produces a 48 kHz fixture.
resampled=array.array('h')
for i,sample in enumerate(samples):
    resampled.extend((sample,(sample+samples[min(i+1,len(samples)-1)])//2))
path=Path('../GuateGeeksAWSVR/Validation/'+('atlas-correction-fixture.wav' if correction else 'atlas-voice-fixture.wav'))
with wave.open(str(path),'wb') as output:
    output.setnchannels(1);output.setsampwidth(2);output.setframerate(48000);output.writeframes(resampled.tobytes())
print('Created synthetic speech fixture: '+str(round(len(resampled)/48000,2))+' seconds')
