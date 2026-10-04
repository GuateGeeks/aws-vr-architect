using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GuateGeeks.AwsVr
{
    public static class InspectionTools
    {
        public static bool SameIdentity(AwsCloudApi.InspectionEntry a, AwsCloudApi.InspectionEntry b) =>
            !string.IsNullOrEmpty(a.id) && !string.IsNullOrEmpty(b.id) ? a.id == b.id : a.title == b.title && a.text == b.text;
        public static bool Matches(AwsCloudApi.InspectionEntry e, string search, string level, string eventId) =>
            (string.IsNullOrEmpty(search) || ((e.text ?? "") + " " + e.title + " " + e.eventId).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) &&
            (string.IsNullOrEmpty(level) || string.Equals(e.level, level, StringComparison.OrdinalIgnoreCase)) &&
            (string.IsNullOrEmpty(eventId) || e.eventId == eventId);
        public static AwsCloudApi.InspectionEntry[] Merge(IEnumerable<AwsCloudApi.InspectionEntry> old, IEnumerable<AwsCloudApi.InspectionEntry> incoming)
        {
            var entries = old.ToList();
            foreach(var entry in incoming) {
                int i = string.IsNullOrEmpty(entry.id) ? -1 : entries.FindIndex(e => e.id == entry.id);
                if(i >= 0) entries[i] = entry; else entries.Add(entry);
            }
            return entries.OrderByDescending(e=>e.timestamp).Take(100).ToArray();
        }
        // Lexical formatting preserves exact numeric/AttributeValue text, never parses numbers as floats.
        public static string Json(string source)
        {
            string value=(source ?? "").Trim(); if(value.Length<2 || (value[0]!='{' && value[0]!='[')) return source ?? "";
            var output=new StringBuilder(); var stack=new Stack<char>(); bool quoted=false, escape=false;
            void Newline() { output.Append('\n'); output.Append(' ',stack.Count*2); }
            foreach(char c in value) {
                if(quoted) { output.Append(c); if(escape) escape=false; else if(c=='\\') escape=true; else if(c=='"') quoted=false; continue; }
                if(c=='"') { quoted=true; output.Append(c); }
                else if(c=='{' || c=='[') { stack.Push(c); if(stack.Count>32) return source; output.Append(c); Newline(); }
                else if(c=='}' || c==']') { if(stack.Count==0 || stack.Pop()!=(c=='}'?'{':'[')) return source; Newline(); output.Append(c); }
                else if(c==',') { output.Append(c); Newline(); }
                else if(c==':') output.Append(": ");
                else if(!char.IsWhiteSpace(c)) output.Append(c);
            }
            return quoted || stack.Count!=0 ? source : output.ToString();
        }
    }
}
