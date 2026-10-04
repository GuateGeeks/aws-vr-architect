using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    [Serializable] public sealed class LambdaCodeDraft
    {
        public const string Starter="import json\n\ndef handler(event, context):\n    return {\"statusCode\": 200, \"body\": json.dumps(event)}\n";
        public string nodeId,source=Starter,baseSource="",revisionId="",eventJson="{\"amount\": 21}",validatedHash="",testedHash="",rollbackVersion="";
        public static string Hash(string source)
        {
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source??""))).Replace("-","").ToLowerInvariant();
        }
        public static bool Readable(string source)=>!string.IsNullOrWhiteSpace(source) && Encoding.UTF8.GetByteCount(source)<=8192 && source.IndexOf('\0')<0;
        public bool Validated=>validatedHash==Hash(source);
        public bool Tested=>testedHash==Hash(source+"\n"+eventJson);
        public void Edit(string value){if(!Readable(value))throw new ArgumentException("Código requerido de hasta 8 KiB.");source=value;validatedHash=testedHash="";}
        public string Difference()
        {
            var before=(baseSource??"").Split('\n');var after=(source??"").Split('\n');var result=new StringBuilder();
            for(int i=0;i<Math.Max(before.Length,after.Length);i++) {
                string old=i<before.Length?before[i]:null,next=i<after.Length?after[i]:null;
                if(old==next)continue;if(old!=null)result.AppendLine("− "+(i+1)+": "+old);if(next!=null)result.AppendLine("+ "+(i+1)+": "+next);
            }
            return result.Length==0?"Sin cambios de código.":result.ToString();
        }
        public void Save(string directory,string identity)
        {
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,Hash(identity)+".json");
            File.WriteAllText(path+".tmp",JsonUtility.ToJson(this),Encoding.UTF8);if(File.Exists(path))File.Replace(path+".tmp",path,null);else File.Move(path+".tmp",path);
        }
        public static LambdaCodeDraft Load(string directory,string identity,string nodeId)
        {
            try {
                string path=Path.Combine(directory,Hash(identity)+".json");
                if(File.Exists(path) && new FileInfo(path).Length<131072) {
                    var value=JsonUtility.FromJson<LambdaCodeDraft>(File.ReadAllText(path));
                    if(value!=null && value.nodeId==nodeId && Readable(value.source)){value.validatedHash=value.testedHash="";return value;}
                }
            }catch(Exception error)when(error is IOException || error is ArgumentException){}
            return new LambdaCodeDraft{nodeId=nodeId};
        }
    }
}
