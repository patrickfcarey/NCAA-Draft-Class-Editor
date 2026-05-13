using System.Collections.Generic;
namespace NcaaDraftEditor.Core;
public static class FieldMap
{
    public const int FirstNameOffset = 6;
    public const int FirstNameLength = 11;
    public const int LastNameOffset  = 17;
    public const int LastNameLength  = 14;
    public static readonly Dictionary<int, string> Names = new()
    {
        { 0, "PFMP" }, { 1, "None" }, { 2, "Random Seed (0-255)" }, { 3, "TGID Bucket" },
        { 4, "TGID" }, { 5, "None" }, { 6, "FirstName" }, { 17, "LastName" },
        { 31, "PYER" }, { 32, "PRSD" }, { 33, "POVR" }, { 34, "PJEN" }, { 35, "PPOS" },
        { 36, "PWGT" }, { 37, "PHGT" }, { 38, "PSTR" }, { 39, "PAGI" }, { 40, "PSPD" }, { 41, "PACC" },
        { 42, "PAWR" }, { 43, "PCTH" }, { 44, "PCAR" }, { 45, "PTHP" }, { 46, "PTHA" },
        { 47, "PKPR" }, { 48, "PKAC" }, { 49, "PBTK" }, { 50, "PTAK" }, { 51, "PIMP" },
        { 52, "PPBK" }, { 53, "PRBK" }, { 54, "PPOE" }, { 55, "PTEN" }, { 56, "PJMP" },
        { 57, "PINJ" }, { 58, "PSTA" }, { 59, "None" }, { 60, "PHED" }, { 61, "PHCL" },
        { 62, "PSKI" }, { 63, "HELM" }, { 64, "PFMK" }, { 65, "PVIS" }, { 66, "PEYE" }, { 67, "PBRE" },
        { 68, "PNEK" }, { 69, "None" }, { 70, "None" }, { 71, "PREB" }, { 72, "PLEB" }, { 73, "None" },
        { 74, "PSLT" }, { 75, "PSLO" }, { 76, "PRWS" }, { 77, "PLWS" }, { 78, "PRHN" }, { 79, "PLHN" },
        { 80, "PRSH" }, { 81, "PLSH" }, { 82, "PMSH" }, { 83, "PFSH" }, { 84, "None" }, { 85, "PSSH" },
    };
    public static string? GetName(int offset) => Names.TryGetValue(offset, out var s) ? s : "";
}
