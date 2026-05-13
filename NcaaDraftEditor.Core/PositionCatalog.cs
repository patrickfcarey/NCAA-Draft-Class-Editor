// Auto-generated from NCAA-DB-Conversion-Reference.xlsx
using System.Collections.Generic;
namespace NcaaDraftEditor.Core;
public static class PositionCatalog {
    public static readonly Dictionary<byte,string> ByCode = new() {
        [ (byte)0 ] = "QB",
        [ (byte)1 ] = "HB",
        [ (byte)2 ] = "FB",
        [ (byte)3 ] = "WR",
        [ (byte)4 ] = "TE",
        [ (byte)5 ] = "LT",
        [ (byte)6 ] = "LG",
        [ (byte)7 ] = "C",
        [ (byte)8 ] = "RG",
        [ (byte)9 ] = "RT",
        [ (byte)10 ] = "LE",
        [ (byte)11 ] = "RE",
        [ (byte)12 ] = "DT",
        [ (byte)13 ] = "LOLB",
        [ (byte)14 ] = "MLB",
        [ (byte)15 ] = "ROLB",
        [ (byte)16 ] = "CB",
        [ (byte)17 ] = "FS",
        [ (byte)18 ] = "SS",
        [ (byte)19 ] = "K",
        [ (byte)20 ] = "P",
    };
    public static List<CodeName> AsList() { var l=new List<CodeName>(); foreach(var kv in ByCode) l.Add(new CodeName(kv.Key, kv.Value)); l.Sort((a,b)=>a.Code.CompareTo(b.Code)); return l; }
}