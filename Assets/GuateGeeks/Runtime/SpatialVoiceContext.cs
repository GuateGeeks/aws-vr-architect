using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GuateGeeks.AwsVr
{
    // Only stable, app-owned targets are recorded. No camera images or UI credential text.
    public sealed class SpatialVoiceContext
    {
        [Serializable] public sealed class Pointer { public string source,nodeId; }
        [Serializable] public sealed class Snapshot {
            public Pointer[] pointers=Array.Empty<Pointer>();
            public string[] gestureNodeIds=Array.Empty<string>();
            public string locationId="";public Vector3 location;
            public bool ambiguous,hasLocation;public float ageSeconds;
        }
        sealed class Sample {public string nodeId;public Vector3? location,anchor,capturedLocation;public float since,seen,locationSeen=-100;}
        sealed class Gesture {public string nodeId;public float time;}
        readonly Dictionary<string,Sample> samples=new Dictionary<string,Sample>();
        readonly List<Gesture> gestures=new List<Gesture>();
        Snapshot frozen;float frozenAt,speechAt=-1;int locationSerial;
        public void Observe(string source,string nodeId,Vector3? location,float now)
        {
            if(!samples.TryGetValue(source,out var sample)){sample=new Sample{since=now};samples[source]=sample;}
            bool changed=sample.nodeId!=nodeId || sample.anchor.HasValue!=location.HasValue || (location.HasValue && Vector3.Distance(sample.anchor.Value,location.Value)>.08f);
            if(changed){sample.since=now;sample.anchor=location;}
            sample.nodeId=nodeId;sample.location=location;sample.seen=now;
            if(location.HasValue && now-sample.since>=.3f){sample.capturedLocation=location;sample.locationSeen=now;}
            if(!string.IsNullOrEmpty(nodeId) && now-sample.since>=.3f) {
                if(gestures.Count==0 || gestures[gestures.Count-1].nodeId!=nodeId)gestures.Add(new Gesture{nodeId=nodeId,time=now});
                else gestures[gestures.Count-1].time=now;
            }
            gestures.RemoveAll(g=>now-g.time>30);if(gestures.Count>12)gestures.RemoveAt(0);
        }
        public void BeginSpeech(float now){if(speechAt<0){speechAt=now;frozen=null;}}
        public void Freeze(float now){frozen=ReadForSpeech(now);frozenAt=now;speechAt=-1;}
        // Live feedback must not stay stuck on the previous utterance's frozen reference.
        public Snapshot Preview(float now)=>speechAt<0?ReadLive(now,now-2):ReadForSpeech(now);
        Snapshot ReadForSpeech(float now)
        {
            float since=speechAt<0?now-2:speechAt-1.5f;
            var value=ReadLive(now,since);
            if(!value.hasLocation && !value.ambiguous && speechAt>=0) {
                // Semantic VAD can finish seconds after the hand is lowered. Retain only
                // stable destinations from this utterance, never from an older request.
                var retained=samples.Values.Where(s=>s.capturedLocation.HasValue && s.locationSeen>=since && now-s.locationSeen<=10).Select(s=>s.capturedLocation.Value).ToArray();
                SetLocation(value,retained);
            }
            return value;
        }
        public Snapshot Read(float now)
        {
            if(frozen==null)return ReadLive(now,now-2);
            // A frozen speech reference is never silently replaced with a later pointer target.
            var copy=JsonUtility.FromJson<Snapshot>(JsonUtility.ToJson(frozen));copy.ageSeconds=Mathf.Max(0,now-frozenAt);
            if(copy.ageSeconds>25){copy.pointers=Array.Empty<Pointer>();copy.gestureNodeIds=Array.Empty<string>();copy.hasLocation=false;copy.locationId="";}
            return copy;
        }
        Snapshot ReadLive(float now,float since)
        {
            var stable=samples.Where(p=>now-p.Value.seen<.25f && now-p.Value.since>=.3f).ToArray();
            var pointers=stable.Where(p=>!string.IsNullOrEmpty(p.Value.nodeId)).Select(p=>new Pointer{source=p.Key,nodeId=p.Value.nodeId}).ToArray();
            var locations=stable.Where(p=>p.Value.location.HasValue).Select(p=>p.Value.location.Value).ToArray();
            var value=new Snapshot{pointers=pointers,gestureNodeIds=gestures.Where(g=>g.time>=since).Select(g=>g.nodeId).Distinct().ToArray(),
                ambiguous=pointers.Select(p=>p.nodeId).Distinct().Count()>1};
            SetLocation(value,locations);return value;
        }
        void SetLocation(Snapshot value,Vector3[] locations)
        {
            bool conflict=locations.Length>1 && locations.Any(p=>Vector3.Distance(p,locations[0])>=.15f);
            value.ambiguous|=conflict;value.hasLocation=locations.Length>0 && !conflict;
            value.location=value.hasLocation?locations[0]:Vector3.zero;value.locationId=value.hasLocation?"point-"+(++locationSerial):"";
        }
        public void Clear(){samples.Clear();gestures.Clear();frozen=null;speechAt=-1;}
    }
}
