using System;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    public static class RealtimeProtocol
    {
        [Serializable] public sealed class Event
        {
            public string type,delta,transcript,call_id,name,arguments,response_id;
            public Response response;
            public Error error;
        }
        [Serializable] public sealed class Error {public string code,message;}
        [Serializable] public sealed class Response {public string id,status;public Usage usage;public StatusDetails status_details;}
        [Serializable] public sealed class StatusDetails {public Error error;}
        [Serializable] public sealed class Usage {public int input_tokens,output_tokens,total_tokens;}
        [Serializable] sealed class Command {public string type;}
        [Serializable] sealed class TextCommand {public string type="conversation.item.create";public TextItem item;}
        [Serializable] sealed class ToolCommand {public string type="conversation.item.create";public ToolItem item;}
        [Serializable] sealed class TextItem {public string type="message",role="user";public Content[] content;}
        [Serializable] sealed class ToolItem {public string type="function_call_output",call_id,output;}
        [Serializable] sealed class Content {public string type="input_text",text;}
        public static Event Parse(string json)
        {
            if(string.IsNullOrEmpty(json) || json.Length>131072) throw new ArgumentException("Evento IA demasiado grande.");
            var result=JsonUtility.FromJson<Event>(json);
            if(result==null || string.IsNullOrEmpty(result.type)) throw new ArgumentException("Evento IA inválido.");
            return result;
        }
        public static string CommandJson(string type)=>JsonUtility.ToJson(new Command{type=type});
        public static string TurnDetectionJson(bool natural)=>"{\"type\":\"session.update\",\"session\":{\"type\":\"realtime\",\"audio\":{\"input\":{\"turn_detection\":"+
            (natural?"{\"type\":\"semantic_vad\",\"eagerness\":\"low\",\"create_response\":false,\"interrupt_response\":false}":"{\"type\":\"server_vad\",\"threshold\":0.5,\"prefix_padding_ms\":300,\"silence_duration_ms\":650,\"create_response\":false,\"interrupt_response\":false}")+"}}}}";
        public static string UserText(string text)=>JsonUtility.ToJson(new TextCommand {item=new TextItem{content=new[]{new Content{text=text}}}});
        public static string ToolOutput(string id,string json)=>JsonUtility.ToJson(new ToolCommand {item=new ToolItem{call_id=id,output=json}});
    }
}
