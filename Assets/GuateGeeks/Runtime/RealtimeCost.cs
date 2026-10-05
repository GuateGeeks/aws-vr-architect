using System;
using System.Collections.Generic;

namespace GuateGeeks.AwsVr
{
    // USD estimates from published rates on 2026-10-04, not an invoice or spend limit.
    // Kept across reconnects for this app execution. Never stores audio or transcripts.
    public sealed class RealtimeCost
    {
        readonly HashSet<string> responses=new HashSet<string>(),transcriptions=new HashSet<string>();
        public double EstimatedUsd {get;private set;}
        public long AudioInput,AudioOutput,TextInput,TextOutput,CachedAudio,CachedText,TranscriptionTokens;
        public bool Incomplete {get;private set;}
        public int Responses=>responses.Count;
        public void AddResponse(string model,RealtimeProtocol.Response response)
        {
            if(response==null || string.IsNullOrEmpty(response.id) || !responses.Add(response.id))return;
            var usage=response.usage;
            if(usage?.input_token_details==null || usage.output_token_details==null){Incomplete=true;return;}
            bool mini=model=="gpt-realtime-2.1-mini";
            if(!mini && model!="gpt-realtime-2.1"){Incomplete=true;return;}
            var input=usage.input_token_details;var output=usage.output_token_details;var cache=input.cached_tokens_details;
            int ca=Math.Min(input.audio_tokens,Math.Max(0,cache?.audio_tokens??0));
            int ct=Math.Min(input.text_tokens,Math.Max(0,cache?.text_tokens??0));
            AudioInput+=input.audio_tokens;TextInput+=input.text_tokens;AudioOutput+=output.audio_tokens;TextOutput+=output.text_tokens;CachedAudio+=ca;CachedText+=ct;
            EstimatedUsd+=((input.audio_tokens-ca)*(mini?10:32)+ca*(mini?.30:.40)+(input.text_tokens-ct)*(mini?.60:4)+ct*(mini?.06:.40)+output.audio_tokens*(mini?20:64)+output.text_tokens*(mini?2.40:24))/1000000d;
            if(input.image_tokens>0 || input.cached_tokens>ca+ct)Incomplete=true;
        }
        public void AddTranscription(string id,RealtimeProtocol.Usage usage)
        {
            if(string.IsNullOrEmpty(id) || !transcriptions.Add(id))return;
            if(usage?.input_token_details==null){Incomplete=true;return;}
            // gpt-4o-mini-transcribe: audio/text input $1.25/M, output $5/M.
            TranscriptionTokens+=usage.total_tokens;
            EstimatedUsd+=(usage.input_token_details.audio_tokens*1.25+usage.input_token_details.text_tokens*1.25+usage.output_tokens*5d)/1000000d;
        }
        public string Summary=>"USD ~"+EstimatedUsd.ToString("0.0000",System.Globalization.CultureInfo.InvariantCulture)+(Incomplete?" · parcial":"")+" · esta ejecución";
    }
}
