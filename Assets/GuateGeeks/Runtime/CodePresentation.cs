using System;
using System.Text;
using System.Text.RegularExpressions;

namespace GuateGeeks.AwsVr
{
    public static class CodePresentation
    {
        public static string Escape(string value) => (value ?? "").Replace("<", "<noparse><</noparse>");
        // Tokenize before escaping so source text can never inject TMP markup.
        public static string Highlight(string value)
        {
            var output = new StringBuilder(); int offset = 0;
            foreach (Match token in Regex.Matches(value ?? "", "#[^\\n]*|'(?:\\\\.|[^'\\\\])*'|\"(?:\\\\.|[^\"\\\\])*\"|\\b(?:def|return|import|from|if|else|elif|for|in|while|try|except|with|as|raise|True|False|None|and|or|not|class|pass)\\b|\\b[0-9]+(?:\\.[0-9]+)?\\b"))
            {
                output.Append(Escape(value.Substring(offset, token.Index-offset)));
                string color = token.Value[0]=='#' ? "8295A8" : token.Value[0]=='\'' || token.Value[0]=='\"' ? "A8DF9B" : char.IsDigit(token.Value[0]) ? "FFCB82" : "C5A0FF";
                output.Append("<color=#"+color+">"+Escape(token.Value)+"</color>"); offset=token.Index+token.Length;
            }
            output.Append(Escape((value ?? "").Substring(offset))); return output.ToString();
        }
        public static string Difference(string before, string after)
        {
            var a=(before ?? "").Split('\n'); var b=(after ?? "").Split('\n');
            if((long)a.Length*b.Length>1000000)
            {
                var bounded=new StringBuilder("AWS / BORRADOR · comparación por línea (archivo con muchas líneas)\n");
                for(int i=0;i<Math.Max(a.Length,b.Length);i++){
                    if(i<a.Length && i<b.Length && a[i]==b[i])continue;
                    if(i<a.Length)bounded.AppendLine("− "+(i+1)+" / —  "+a[i]);
                    if(i<b.Length)bounded.AppendLine("+ — / "+(i+1)+"  "+b[i]);
                }
                return bounded.ToString();
            }
            // LCS aligns insertions and deletions. At 8 KiB, this bounded table is small.
            var lengths=new ushort[a.Length+1,b.Length+1];
            for(int i=a.Length-1;i>=0;i--)for(int j=b.Length-1;j>=0;j--)
                lengths[i,j]=(ushort)(a[i]==b[j]?1+lengths[i+1,j+1]:Math.Max(lengths[i+1,j],lengths[i,j+1]));
            var result=new StringBuilder(); int x=0,y=0; bool changed=false;
            while(x<a.Length || y<b.Length)
            {
                if(x<a.Length && y<b.Length && a[x]==b[y]){result.AppendLine("  "+(x+1)+" / "+(y+1)+"  "+a[x]);x++;y++;}
                else if(y<b.Length && (x==a.Length || lengths[x,y+1]>=lengths[x+1,y])){result.AppendLine("+ — / "+(++y)+"  "+b[y-1]);changed=true;}
                else {result.AppendLine("− "+(++x)+" / —  "+a[x-1]);changed=true;}
            }
            return changed?"AWS / BORRADOR · − eliminado · + agregado\n"+result:"Sin cambios de código.";
        }
    }
}
