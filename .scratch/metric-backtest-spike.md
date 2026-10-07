# Spike: backtest form metrics against own history

Run after step 4 of [the build order](../docs/build-order.md) lands the
backfilled decade of history. The thresholds in
[ADR 0009](../docs/adr/0009-trainingload-has-two-axes.md) and spec 04's response
shapes are group-level findings; this spike checks them against the one athlete
they serve.

Questions, each answerable from the store alone:

- Did systemic TrainingLoad spikes precede the weeks that actually went badly —
  illness, injury Constraints, missed training?
- Does the HRV weekly average leaving its baseline band flag those same weeks,
  and with what lead time?
- Does the per-Exercise EstimatedOneRepMax trend match remembered strength
  changes across old rep-scheme transitions?
- Do the stored-but-never-exposed fields — sleep stages, body battery, stress —
  add any signal the exposed fields lack? Exposure is earned here or nowhere.

Output: confirmed or adjusted flag thresholds, recorded as an update to ADR
0009. Throwaway analysis code lives in `spikes/`, committed for the record,
never imported.
