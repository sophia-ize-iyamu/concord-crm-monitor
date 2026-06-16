using Microsoft.Data.Sqlite;

namespace Concord;

/// Builds the CRM schema and seeds two reproducible snapshots of the same pipeline:
/// PRE  = the state before a monthly release, taken as the known-good baseline,
/// POST = the state after the release, the candidate that UAT has to clear.
/// Faults are injected into POST on purpose so the rule engine and the regression pass have
/// something to catch (the same honest approach used in Takt and the Market Data Health Monitor).
/// Real CRM data is not public at the record level, so the pipeline is hand-built to be illustrative,
/// not a real client book. What is real is the method: the schema, the reference data, the SQL rules,
/// the regression diff, and the defect-triage lifecycle.
public static class Schema
{
    public static void Build(SqliteConnection db)
    {
        Exec(db,
            "DROP TABLE IF EXISTS defects;" +
            "DROP TABLE IF EXISTS opportunities;" +
            "DROP TABLE IF EXISTS accounts;" +
            "DROP TABLE IF EXISTS ref_stages;" +
            "DROP TABLE IF EXISTS ref_currencies;" +
            // Reference data: the canonical, governed lists every record is held to.
            @"CREATE TABLE ref_stages(stage TEXT PRIMARY KEY, sort_order INTEGER NOT NULL);
              CREATE TABLE ref_currencies(code TEXT PRIMARY KEY);
              CREATE TABLE accounts(
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, region TEXT NOT NULL, owner TEXT);
              -- The Opportunity-to-Engagement record. Held in two snapshots via release_tag.
              CREATE TABLE opportunities(
                id INTEGER PRIMARY KEY,
                release_tag TEXT NOT NULL,        -- PRE (baseline) or POST (release candidate)
                opp_number  TEXT NOT NULL,        -- stable business key across snapshots
                account_id  INTEGER,              -- FK to accounts (may be orphaned by a bad release)
                name        TEXT,
                stage       TEXT,                 -- must be in ref_stages
                amount      REAL,
                currency    TEXT,                 -- must be in ref_currencies
                close_date  TEXT,                 -- ISO yyyy-MM-dd
                owner       TEXT,
                status      TEXT);                -- Open / Won / Lost
              -- Defect register with a full triage lifecycle: OPEN -> TRIAGED -> RESOLVED -> VALIDATED.
              CREATE TABLE defects(
                id INTEGER PRIMARY KEY, entity_type TEXT NOT NULL, entity_ref TEXT NOT NULL,
                rule_code TEXT NOT NULL, severity TEXT NOT NULL,   -- HIGH / MED / LOW
                status TEXT NOT NULL,                              -- OPEN / TRIAGED / RESOLVED / VALIDATED
                opened_ts TEXT NOT NULL, resolved_ts TEXT);        -- nulls until resolved");
        Seed(db);
    }

    static void Seed(SqliteConnection db)
    {
        Exec(db, @"INSERT INTO ref_stages(stage,sort_order) VALUES
            ('Lead',1),('Qualified',2),('Proposal',3),('Negotiation',4),('Won',5),('Lost',6);");
        Exec(db, @"INSERT INTO ref_currencies(code) VALUES ('CAD'),('USD'),('EUR'),('GBP');");

        Exec(db, @"INSERT INTO accounts(id,name,region,owner) VALUES
            (1,'Meridian Bank','Canada','A. Osei'),
            (2,'Northwind Retail','Canada','J. Park'),
            (3,'Helios Energy','US','A. Osei'),
            (4,'Cedar Health','Canada','M. Diaz'),
            (5,'Atlas Logistics','UK','J. Park');");

        // ---- PRE snapshot: the clean baseline. Every row passes every rule. ----
        var pre = new (string No, int Acct, string Name, string Stage, double Amt, string Ccy, int CloseOffset, string Owner, string Status)[]
        {
            ("OPP-2001", 1, "Meridian core banking refresh",  "Negotiation", 480000, "CAD",  30, "A. Osei", "Open"),
            ("OPP-2002", 2, "Northwind POS rollout",          "Proposal",    120000, "CAD",  45, "J. Park", "Open"),
            ("OPP-2003", 3, "Helios field data platform",     "Won",         260000, "USD", -10, "A. Osei", "Won"),
            ("OPP-2004", 4, "Cedar patient portal",           "Qualified",    90000, "CAD",  60, "M. Diaz", "Open"),
            ("OPP-2005", 5, "Atlas fleet analytics",          "Lead",         55000, "GBP",  75, "J. Park", "Open"),
            ("OPP-2006", 1, "Meridian fraud analytics",       "Proposal",    210000, "CAD",  50, "A. Osei", "Open"),
        };
        foreach (var o in pre)
            AddOpp(db, "PRE", o.No, o.Acct, o.Name, o.Stage, o.Amt, o.Ccy, o.CloseOffset, o.Owner, o.Status);

        // ---- POST snapshot: the release candidate, with regressions injected. ----
        // Same six records, but the release damaged some of them. Each fault maps to one rule.
        var post = new (string No, int? Acct, string Name, string Stage, double? Amt, string Ccy, int CloseOffset, string Owner, string Status)[]
        {
            ("OPP-2001", 1, "Meridian core banking refresh",  "Negotiation", 480000, "CAD",  30, "A. Osei", "Open"),
            ("OPP-2002", 2, "Northwind POS rollout",          "Proposal",    120000, "CAD",  45, "",        "Open"),   // MISSING_OWNER (regression)
            ("OPP-2003", 3, "Helios field data platform",     "Won",         null,    "USD", -10, "A. Osei", "Won"),    // BAD_AMOUNT  (regression: Won with no value)
            ("OPP-2004", 4, "Cedar patient portal",           "Discovery",    90000, "CAD",  60, "M. Diaz", "Open"),   // INVALID_STAGE (off-reference value)
            ("OPP-2005", null, "Atlas fleet analytics",       "Lead",         55000, "GBP",  75, "J. Park", "Open"),   // ORPHAN_ACCOUNT (FK dropped)
            ("OPP-2006", 1, "Meridian fraud analytics",       "Proposal",    210000, "CAD", -15, "A. Osei", "Open"),   // STALE_OPEN (close date pushed into the past)
            ("OPP-2007", 2, "Northwind loyalty pilot",        "Qualified",    40000, "BTC",  20, "J. Park", "Open"),   // BAD_CURRENCY (new record, off-reference ccy)
            ("OPP-2008", 2, "Northwind POS rollout",          "Proposal",    120000, "CAD",  45, "J. Park", "Open"),   // DUP_OPP (same account + name as OPP-2002)
        };
        foreach (var o in post)
            AddOppNullable(db, "POST", o.No, o.Acct, o.Name, o.Stage, o.Amt, o.Ccy, o.CloseOffset, o.Owner, o.Status);

        // ---- Pre-existing defect register, so backlog and cycle-time KPIs are real. ----
        // A mix of lifecycle states from prior release cycles; timestamps drive the cycle-time metric.
        Exec(db, @"INSERT INTO defects(entity_type,entity_ref,rule_code,severity,status,opened_ts,resolved_ts) VALUES
            ('opportunity','OPP-1804','MISSING_OWNER','MED','VALIDATED','2026-05-04','2026-05-06'),
            ('opportunity','OPP-1811','INVALID_STAGE','HIGH','VALIDATED','2026-05-07','2026-05-12'),
            ('account','ACCT-22','BAD_CURRENCY','LOW','RESOLVED','2026-05-19','2026-05-21'),
            ('opportunity','OPP-1830','STALE_OPEN','MED','TRIAGED','2026-05-28',NULL),
            ('opportunity','OPP-1842','BAD_AMOUNT','HIGH','OPEN','2026-06-02',NULL);");
    }

    static void AddOpp(SqliteConnection db, string tag, string no, int acct, string name, string stage,
                       double amt, string ccy, int closeOffset, string owner, string status)
        => AddOppNullable(db, tag, no, acct, name, stage, amt, ccy, closeOffset, owner, status);

    static void AddOppNullable(SqliteConnection db, string tag, string no, int? acct, string name, string stage,
                               double? amt, string ccy, int closeOffset, string owner, string status)
    {
        var closeDate = DateTime.Today.AddDays(closeOffset).ToString("yyyy-MM-dd");
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"INSERT INTO opportunities
            (release_tag,opp_number,account_id,name,stage,amount,currency,close_date,owner,status)
            VALUES ($tag,$no,$acct,$name,$stage,$amt,$ccy,$close,$owner,$status);";
        cmd.Parameters.AddWithValue("$tag", tag);
        cmd.Parameters.AddWithValue("$no", no);
        cmd.Parameters.AddWithValue("$acct", (object?)acct ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$stage", stage);
        cmd.Parameters.AddWithValue("$amt", (object?)amt ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$ccy", ccy);
        cmd.Parameters.AddWithValue("$close", closeDate);
        cmd.Parameters.AddWithValue("$owner", owner);
        cmd.Parameters.AddWithValue("$status", status);
        cmd.ExecuteNonQuery();
    }

    static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
