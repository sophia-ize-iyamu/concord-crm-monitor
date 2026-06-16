# Concord: a CRM data-quality and release-testing console

What does a team supporting a CRM platform have to answer before a monthly release goes live? Whether
the release broke any records. Which records, and why. Whether core data still matches the governed
reference lists. Whether the open-defect backlog and cycle time are healthy enough to promote. And, in
the end, a single call: ship it, watch it, or hold it. Concord is a small, working model of that, built
end to end in **C# and SQL**.

It is a monitoring-and-support tool for a CRM / Opportunity-to-Engagement (O2E) pipeline: model the
records in a database, validate them against governed reference data, run a UAT-style regression pass to
isolate what a release broke, triage each break into a defect register, and gate the release.

## What it does
- **Models a CRM pipeline in SQL**: accounts, opportunities through the O2E stages (Lead to Won/Lost),
  governed reference data (the canonical stage and currency lists), and a defect register with a full
  triage lifecycle (OPEN to TRIAGED to RESOLVED to VALIDATED).
- **Holds two snapshots** of the same pipeline: **PRE** (the known-good baseline before a release) and
  **POST** (the release candidate UAT has to clear).
- **Validates every record against reference data**, in SQL, with seven rules: missing owner, off-list
  stage, a Won opportunity with no amount, a stale open opportunity, an orphaned account, an off-list
  currency, and a duplicate opportunity.
- **Runs a regression pass**: a finding present in the candidate but not in the baseline is a regression
  the release introduced. This separates "the release broke this" from pre-existing data debt.
- **Triages and gates**: each regression is written to the defect register as a new OPEN defect, and the
  release is gated **OK / WATCH / HOLD** by the worst severity found (a HIGH regression holds the release).
- **Reports release-health KPIs**: open backlog, high-severity open count, mean cycle time (open to
  resolved, in days), and validation rate.
- **Writes a release report** to `out/findings.md`.

## The data
CRM record-level data is not public, so the project seeds **two reproducible synthetic snapshots** with
regressions injected into the release candidate on purpose (the same honest approach as Takt and my
Market Data Health Monitor). The injected regressions are one per rule: a blanked owner, a stage set
off-reference, a Won deal with its amount nulled, a close date pushed into the past, a dropped account
foreign key, an off-list currency, and a duplicated opportunity.

The figures are illustrative, not a real client book. What is real is the **method**: the schema, the
reference data, the SQL rules, the regression diff, and the triage lifecycle.

## Run it
Requires the .NET SDK (built and tested on .NET 10).
```bash
dotnet run
```
You will see the regression table, the release gate, and the release-health KPIs in the console, and a
full `out/findings.md` report.

## Concepts and references
- **Data quality dimensions** (completeness, validity, consistency, uniqueness): the rule catalogue maps
  to the DAMA-DMBOK data-quality dimensions used in data governance.
- **Reference / master data**: the governed stage and currency lists are the reference data every record
  is held to; off-list values are validity failures (DAMA reference-and-master-data management).
- **Regression testing and release gating**: comparing a release candidate to a known-good baseline and
  blocking promotion on a severity threshold is standard UAT / release-readiness practice.
- **Defect lifecycle**: OPEN to TRIAGED to RESOLVED to VALIDATED mirrors a standard defect-tracking flow,
  which is what the cycle-time and validation-rate KPIs measure.

## Honest limits
The snapshots are synthetic, so the figures are illustrative. This models the *core* of CRM data-quality
and release support (validation, regression isolation, triage, release gating, KPIs); it is not a full
platform (no live CRM connector, no workflow engine, no UI). The point is to show I understand what
supporting a CRM platform through a release cycle requires and can build and query it in C# and SQL.
