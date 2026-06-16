using Microsoft.Data.Sqlite;
using System.Text;
using Concord;

// Concord: a CRM / Opportunity-to-Engagement data-quality and release-testing console.
// It models a CRM pipeline in SQL, validates every record against governed reference data, runs a
// UAT-style regression pass (release candidate vs known-good baseline) to isolate what a release
// broke, triages each break into the defect register, and gates the release on a health verdict:
// OK (release-ready), WATCH, or HOLD.

var dbPath = Path.Combine(Environment.CurrentDirectory, "concord.db");
if (File.Exists(dbPath)) File.Delete(dbPath);   // rebuild each run so output is reproducible

using var db = new SqliteConnection($"Data Source={dbPath}");
db.Open();
Schema.Build(db);

// Validate both snapshots, then isolate the regressions the release introduced.
var baseline  = Rules.Validate(db, "PRE");
var candidate = Rules.Validate(db, "POST");
var regressions = Rules.Regressions(baseline, candidate);

Console.WriteLine("== Concord: release data-quality check ==");
Console.WriteLine($"Baseline (PRE) findings:        {baseline.Count}");
Console.WriteLine($"Release candidate (POST):       {candidate.Count}");
Console.WriteLine($"Regressions introduced by release: {regressions.Count}\n");

Console.WriteLine("== Regressions (what this release broke) ==");
Console.WriteLine($"{"Opportunity",-12}{"Severity",-9}{"Rule",-16}Detail");
foreach (var f in regressions)
    Console.WriteLine($"{f.OppNumber,-12}{f.Severity,-9}{f.RuleCode,-16}{f.Detail}");

// Triage: append each regression to the defect register as a new OPEN defect.
int high = regressions.Count(f => f.Severity == "HIGH");
int med  = regressions.Count(f => f.Severity == "MED");
foreach (var f in regressions)
{
    using var cmd = db.CreateCommand();
    cmd.CommandText = @"INSERT INTO defects(entity_type,entity_ref,rule_code,severity,status,opened_ts,resolved_ts)
        VALUES ('opportunity',$ref,$rule,$sev,'OPEN',date('now'),NULL);";
    cmd.Parameters.AddWithValue("$ref", f.OppNumber);
    cmd.Parameters.AddWithValue("$rule", f.RuleCode);
    cmd.Parameters.AddWithValue("$sev", f.Severity);
    cmd.ExecuteNonQuery();
}

// Release gate: HIGH-severity regressions hold the release; MED puts it on watch.
string verdict = high > 0 ? "HOLD" : med > 0 ? "WATCH" : "OK";
string gate = verdict switch
{
    "HOLD"  => $"HOLD release: {high} high-severity regression(s) must be fixed and re-tested.",
    "WATCH" => $"WATCH: {med} medium-severity regression(s); fix or accept with sign-off.",
    _       => "OK: no regressions; release is clear to promote."
};
Console.WriteLine($"\n== Release gate ==\n{verdict}: {gate}");

var kpis = Rules.ReleaseHealth(db);
Console.WriteLine("\n== Release-health KPIs ==");
foreach (var k in kpis) Console.WriteLine($"{k.Metric,-44}{k.Value}");

WriteReport(baseline, candidate, regressions, verdict, gate, kpis);
Console.WriteLine("\nWrote out/findings.md");

void WriteReport(List<Finding> baseline, List<Finding> candidate, List<Finding> regressions,
                 string verdict, string gate, List<KpiRow> kpis)
{
    var dir = Path.Combine(Environment.CurrentDirectory, "out");
    Directory.CreateDirectory(dir);
    var sb = new StringBuilder();
    sb.AppendLine("# Concord release report\n");
    sb.AppendLine("A monthly release is validated against governed reference data, then compared to the");
    sb.AppendLine("known-good baseline to isolate regressions. Each regression is triaged into the defect");
    sb.AppendLine("register, and the release is gated OK / WATCH / HOLD.\n");

    sb.AppendLine("## Summary\n");
    sb.AppendLine($"- Baseline (PRE) findings: **{baseline.Count}**");
    sb.AppendLine($"- Release candidate (POST) findings: **{candidate.Count}**");
    sb.AppendLine($"- Regressions introduced by the release: **{regressions.Count}**");
    sb.AppendLine($"- Release gate: **{verdict}** ({gate})\n");

    sb.AppendLine("## Regressions (what this release broke)\n");
    sb.AppendLine("| Opportunity | Severity | Rule | Detail |");
    sb.AppendLine("|---|---|---|---|");
    foreach (var f in regressions)
        sb.AppendLine($"| {f.OppNumber} | {f.Severity} | {f.RuleCode} | {f.Detail} |");

    sb.AppendLine("\n## Release-health KPIs (defect register)\n");
    sb.AppendLine("| Metric | Value |");
    sb.AppendLine("|---|---|");
    foreach (var k in kpis) sb.AppendLine($"| {k.Metric} | {k.Value} |");

    sb.AppendLine("\n## Rule catalogue\n");
    sb.AppendLine("| Rule | Severity | What it checks |");
    sb.AppendLine("|---|---|---|");
    sb.AppendLine("| MISSING_OWNER | MED | every opportunity has an owner |");
    sb.AppendLine("| INVALID_STAGE | HIGH | stage is in the governed reference list |");
    sb.AppendLine("| BAD_AMOUNT | HIGH | a Won opportunity carries a positive amount |");
    sb.AppendLine("| STALE_OPEN | MED | an Open opportunity has a future close date |");
    sb.AppendLine("| ORPHAN_ACCOUNT | HIGH | account_id resolves to a real account |");
    sb.AppendLine("| BAD_CURRENCY | MED | currency is in the reference list |");
    sb.AppendLine("| DUP_OPP | LOW | no duplicate opportunity on the same account |");

    File.WriteAllText(Path.Combine(dir, "findings.md"), sb.ToString());
}
