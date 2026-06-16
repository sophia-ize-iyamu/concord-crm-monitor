# Concord release report

A monthly release is validated against governed reference data, then compared to the
known-good baseline to isolate regressions. Each regression is triaged into the defect
register, and the release is gated OK / WATCH / HOLD.

## Summary

- Baseline (PRE) findings: **0**
- Release candidate (POST) findings: **7**
- Regressions introduced by the release: **7**
- Release gate: **HOLD** (HOLD release: 3 high-severity regression(s) must be fixed and re-tested.)

## Regressions (what this release broke)

| Opportunity | Severity | Rule | Detail |
|---|---|---|---|
| OPP-2004 | HIGH | INVALID_STAGE | stage 'Discovery' is not in the reference stage list |
| OPP-2003 | HIGH | BAD_AMOUNT | Won opportunity has no positive amount |
| OPP-2005 | HIGH | ORPHAN_ACCOUNT | account_id has no matching account row |
| OPP-2002 | MED | MISSING_OWNER | owner is blank |
| OPP-2006 | MED | STALE_OPEN | Open opportunity with a close date in the past (2026-06-01) |
| OPP-2007 | MED | BAD_CURRENCY | currency 'BTC' is not in the reference list |
| OPP-2008 | LOW | DUP_OPP | duplicate of another opportunity (same account and name) |

## Release-health KPIs (defect register)

| Metric | Value |
|---|---|
| Open backlog (OPEN + TRIAGED) | 9 |
| High-severity open | 4 |
| Mean cycle time, days (resolved defects) | 3.0 |
| Validation rate | 67% |

## Rule catalogue

| Rule | Severity | What it checks |
|---|---|---|
| MISSING_OWNER | MED | every opportunity has an owner |
| INVALID_STAGE | HIGH | stage is in the governed reference list |
| BAD_AMOUNT | HIGH | a Won opportunity carries a positive amount |
| STALE_OPEN | MED | an Open opportunity has a future close date |
| ORPHAN_ACCOUNT | HIGH | account_id resolves to a real account |
| BAD_CURRENCY | MED | currency is in the reference list |
| DUP_OPP | LOW | no duplicate opportunity on the same account |
