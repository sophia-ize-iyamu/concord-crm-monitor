using Microsoft.Data.Sqlite;

namespace Concord;

public record Finding(string OppNumber, string RuleCode, string Severity, string Detail);
public record KpiRow(string Metric, string Value);

/// The data-quality and release-testing checks, computed in SQL against a snapshot.
/// Each rule is one governed expectation; a row that breaks it becomes a finding, and a finding
/// becomes a defect. The regression pass compares the release candidate (POST) against the
/// known-good baseline (PRE) to isolate what the release itself broke.
public static class Rules
{
    // One SQL predicate per rule. Each returns the offending opp_number plus a human detail.
    // Severity reflects how much a bad release should be held back for it.
    static readonly (string Code, string Severity, string Sql)[] Checks =
    {
        ("MISSING_OWNER", "MED", @"
            SELECT opp_number, 'owner is blank' AS detail
            FROM opportunities WHERE release_tag=$tag AND (owner IS NULL OR TRIM(owner)='')"),

        ("INVALID_STAGE", "HIGH", @"
            SELECT o.opp_number, 'stage ''' || o.stage || ''' is not in the reference stage list' AS detail
            FROM opportunities o
            LEFT JOIN ref_stages r ON r.stage = o.stage
            WHERE o.release_tag=$tag AND r.stage IS NULL"),

        ("BAD_AMOUNT", "HIGH", @"
            SELECT opp_number, 'Won opportunity has no positive amount' AS detail
            FROM opportunities
            WHERE release_tag=$tag AND status='Won' AND (amount IS NULL OR amount<=0)"),

        ("STALE_OPEN", "MED", @"
            SELECT opp_number, 'Open opportunity with a close date in the past (' || close_date || ')' AS detail
            FROM opportunities
            WHERE release_tag=$tag AND status='Open' AND close_date < date('now')"),

        ("ORPHAN_ACCOUNT", "HIGH", @"
            SELECT o.opp_number, 'account_id has no matching account row' AS detail
            FROM opportunities o
            LEFT JOIN accounts a ON a.id = o.account_id
            WHERE o.release_tag=$tag AND (o.account_id IS NULL OR a.id IS NULL)"),

        ("BAD_CURRENCY", "MED", @"
            SELECT o.opp_number, 'currency ''' || o.currency || ''' is not in the reference list' AS detail
            FROM opportunities o
            LEFT JOIN ref_currencies c ON c.code = o.currency
            WHERE o.release_tag=$tag AND c.code IS NULL"),

        ("DUP_OPP", "LOW", @"
            SELECT o.opp_number, 'duplicate of another opportunity (same account and name)' AS detail
            FROM opportunities o
            WHERE o.release_tag=$tag AND EXISTS (
                SELECT 1 FROM opportunities o2
                WHERE o2.release_tag=o.release_tag AND o2.account_id=o.account_id
                  AND o2.name=o.name AND o2.id < o.id)"),
    };

    /// Run every rule against one snapshot and collect the findings.
    public static List<Finding> Validate(SqliteConnection db, string tag)
    {
        var findings = new List<Finding>();
        foreach (var c in Checks)
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = c.Sql;
            cmd.Parameters.AddWithValue("$tag", tag);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                findings.Add(new Finding(r.GetString(0), c.Code, c.Severity, r.GetString(1)));
        }
        return findings;
    }

    /// Regression = a finding present in the release candidate but NOT in the baseline.
    /// A regression is something this release introduced; everything else is pre-existing.
    public static List<Finding> Regressions(List<Finding> baseline, List<Finding> candidate)
    {
        var baselineKeys = new HashSet<string>(baseline.Select(f => f.OppNumber + "|" + f.RuleCode));
        return candidate
            .Where(f => !baselineKeys.Contains(f.OppNumber + "|" + f.RuleCode))
            .OrderBy(f => f.Severity == "HIGH" ? 0 : f.Severity == "MED" ? 1 : 2)
            .ToList();
    }

    /// Release-health KPIs over the standing defect register: backlog, defects by severity, mean
    /// cycle time (open to resolved, in days), and validation rate. These are the numbers a release
    /// readiness review asks for.
    public static List<KpiRow> ReleaseHealth(SqliteConnection db)
    {
        var rows = new List<KpiRow>();
        rows.Add(new KpiRow("Open backlog (OPEN + TRIAGED)", Scalar(db,
            "SELECT COUNT(*) FROM defects WHERE status IN ('OPEN','TRIAGED')")));
        rows.Add(new KpiRow("High-severity open", Scalar(db,
            "SELECT COUNT(*) FROM defects WHERE severity='HIGH' AND status IN ('OPEN','TRIAGED')")));
        rows.Add(new KpiRow("Mean cycle time, days (resolved defects)", Scalar(db,
            "SELECT printf('%.1f', AVG(julianday(resolved_ts) - julianday(opened_ts))) " +
            "FROM defects WHERE resolved_ts IS NOT NULL")));
        rows.Add(new KpiRow("Validation rate", Pct(db,
            "SELECT COUNT(*) FROM defects WHERE status='VALIDATED'",
            "SELECT COUNT(*) FROM defects WHERE status IN ('RESOLVED','VALIDATED')")));
        return rows;
    }

    static string Scalar(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? "n/a" : v.ToString()!;
    }

    static string Pct(SqliteConnection db, string numSql, string denSql)
    {
        double num = double.Parse(Scalar(db, numSql)), den = double.Parse(Scalar(db, denSql));
        return den == 0 ? "n/a" : $"{num / den:P0}";
    }
}
