# Literature pass: metric designs for a personal training-coach system

Date: 2026-10-07. Scope: four questions, primary sources where findable, error magnitudes, honest disagreement.

---

## Q-A. Estimated 1RM (e1RM) prediction equations

### Accuracy by rep range (reps taken to failure)

- **LeSuer, McCormick, Mayhew, Wasserstein & Arnold (1997), *J Strength Cond Res* 11(4)** — 67 untrained college students, bench press / squat / deadlift, 7 equations (Epley, Brzycki, Lander, Mayhew, O'Conner, Wathen, Lombardi), reps-to-failure ≤10. Correlations uniformly high (r > 0.95). Bench and squat predictions were close (most equations within a few percent; Mayhew and Wathen best), but **all equations underestimated deadlift 1RM by 9–14% (22–34 lb)**. Conclusion in-paper: RTF ≤10 can predict 1RM acceptably for bench/squat; not for deadlift.
- **Ware, Clemens, Mayhew & Johnston (1995), *J Strength Cond Res*** — 45 college football players, bench + squat. Mean errors: Mayhew −3.1 kg, Epley +4.8 kg, Lander +14.1 kg, Brzycki +14.2 kg (the large overestimates were on the squat). Shows equation choice matters at the 5–15 kg level in trained populations.
- **Reynolds, Gordon & Robergs (2006), *J Strength Cond Res* 20(3):584–592** — 70 adults, chest press and leg press, predictions from 5RM, 10RM, 20RM. **5RM gave the best prediction (R² = 0.993 chest press, 0.974 leg press); accuracy degraded materially at 10RM and badly at 20RM.** Recommendation: use ≤10 reps in linear equations.
- Convergent picture across reviews (e.g. the UNM accuracy review; Wood et al. 2002; multiple bench-press validations): **1–5 reps-to-failure: typical error ±2–3% (~2–5 kg)**; **6–10 reps: ±5% and equation-dependent (Epley tends to behave better than Brzycki above ~6 reps; Brzycki better ≤6)**; **10–15 reps: errors grow to ~10%+ and are systematically biased — most validation studies simply stop at 10 reps because beyond that the rep–%1RM relationship is dominated by muscular endurance, exercise type, and the individual**. Deadlift and lower-body machine exercises carry additional systematic bias (LeSuer: −9 to −14%).

### Sets not taken to failure

The equations assume reps-to-failure. For non-failure working sets the usual fix is `effective reps = reps performed + RIR`, which makes the estimate only as good as the RIR estimate:

- **Steele et al. (2017), *PeerJ* 5:e4105** — 141 participants predicting reps to momentary failure: experienced trainees underpredicted by ~1–2 reps, inexperienced by ~4–5 reps.
- **Zourdos et al. (2019/2021, incl. Hackett's estimated-reps-to-failure line of work)** — RIR accuracy improves with proximity to failure: reasonable at 1 RIR, meaningfully worse at ≥3–5 RIR. A 2025 replication (Casanova et al.) confirms the gradient.
- Net effect: a 12-rep set logged at "3 RIR" that was really 5 RIR underestimates e1RM by roughly 5–6%. The error is directional (people underestimate how much they have left, so e1RM from non-failure sets is biased **low**) and shrinks as the lifter gains experience and trains closer to failure.

### What practitioners/researchers use instead

- **Velocity-based 1RM estimation** — individualized load–velocity profiles predict bench-press 1RM within 2.7–3.7% (Pérez-Castilla et al. 2021 and related García-Ramos work) but are **invalid for deadlift** (Ruf, Chéry & Taylor 2018: significant, meaningful underestimation) and require a velocity device; not practical from Garmin data.
- **Best-set e1RM tracking** (take the highest e1RM across a session's sets per exercise) — the de facto standard in powerlifting-adjacent apps and RPE-based programming (Helms/Zourdos school); reduces noise from back-off sets.
- **Volume-load (sets × reps × kg)** — robust to the failure assumption but conflates work with strength: it rises when you add a set even at unchanged strength, so it answers "how much did I do", not "am I stronger".

### Conclusion (Q-A)

**e1RM trend per exercise IS defensible for longitudinal monitoring, with caveats:** (X) compute it only from sets of ≤10 reps where RIR is logged, using reps+RIR, and prefer the best set of the session; (Y) treat the level as biased (typically a few % low on non-failure sets, ~10% low on deadlift-pattern lifts) but the within-exercise, within-rep-scheme **trend** as meaningful, because the bias is directional and roughly stable for a given lifter at a given proximity to failure; (Z) flag, don't silently absorb, block transitions that change rep scheme (e.g. 5s → 12s) — the equation-dependent offset between rep ranges (up to ~10%) will show up as a spurious step in the trend, so compare trend slopes within blocks rather than raw values across them. Epley or Mayhew are the safest single-equation defaults; above 10–12 reps, downweight or discard the estimate.

---

## Q-B. Acute:Chronic Workload Ratio (ACWR)

### Origin and critique

- **Gabbett (2016), *Br J Sports Med*** — the "training–injury prevention paradox": sweet spot 0.8–1.3, elevated injury risk >1.5.
- **Impellizzeri, Tenan, Kempton, Novak & Coutts (2020), *Int J Sports Physiol Perform*: "Acute:Chronic Workload Ratio: Conceptual Issues and Fundamental Pitfalls"** and **Impellizzeri et al. (2020), *Sports Med*: "What Role Do Chronic Workloads Play…? Time to Dismiss ACWR and Its Underlying Theory"** — mathematical coupling of numerator and denominator, the U-shaped "sweet spot" is an artifact of binning continuous data (reanalysis treating ACWR as continuous and removing sparse-bin outliers made the association disappear), unjustified causal claims from observational single-cohort data, arbitrary 7/28-day windows. They explicitly recommend the framework be **dismissed** and consensus statements (incl. IOC 2016, which had endorsed ACWR) be updated.
- **Gabbett and colleagues' responses** (2019–2021 editorials/debates, e.g. the Frontiers in Physiology 2021 editorial "Acute:Chronic Workload Ratio: Is There Scientific Evidence?") concede it is **not an injury predictor** and reframe it as one descriptive load-monitoring tool among several; the disagreement over whether even that use is salvageable remains open.
- **Maupin, Schram, Canetti & Orr (2020), *Open Access J Sports Med*** — systematic review, 22 cohort studies: associations inconsistent, methods heterogeneous.
- **2025 systematic review and meta-analysis (PubMed 41029871)** — pooled association between high ACWR and injury exists in some sports, but heterogeneity, calculation differences, and probable publication bias leave predictive utility inconclusive.

### Rolling average vs EWMA

**Williams, West, Cross & Kemp (2017), *Br J Sports Med*** — EWMA-based ACWR was more sensitive than the 7:28 rolling average for detecting elevated injury likelihood at high ratios (>2.0) and explained more injury variance. If any ratio is computed, EWMA is the better-supported form (it also partially mitigates the equal-weighting criticism). Note Lolli et al. (2019) additionally showed the coupled ratio is mathematically distorted and proposed uncoupled calculation.

### Conclusion (Q-B)

The "spike flag only" stance is defensible — in fact it is the **only** defensible use left: ACWR must never be a target to optimise (the 0.8–1.3 sweet spot is a statistical artifact), and even as a flag it has no validated threshold, so treat >~1.5 as "ask about fatigue/soreness and avoid stacking another hard week", not as a predicted injury. Prefer EWMA (or simply week-over-week absolute load change) over the coupled 7:28 rolling ratio, and present it as a description of load progression, never as injury risk. For one recreational athlete at ~3 sessions/week the denominator is noisy, which strengthens the case for flag-only, conservative use.

---

## Q-C. Wrist-worn sleep staging vs polysomnography (Garmin)

### Sleep/wake and duration

- **Chinoy et al. (2021), *SLEEP* 44(5):zsaa291** — 34 healthy adults, 3 PSG nights (one disrupted), 7 consumer devices incl. **Garmin Fenix 5S and Garmin Vivosmart 3**. Epoch-by-epoch sleep **sensitivity was high for all devices (≥0.93)** but wake **specificity was low: Garmin Fenix 5S 0.18, Vivosmart 3 0.19 — the lowest in the study** (other devices up to ~0.54). The Garmin devices performed **worse than research actigraphy** on sleep/wake measures and systematically overestimated TST/sleep efficiency, especially on the disrupted night. Devices generally did worse on poor-sleep nights.
- **JMIR mHealth 2024 systematic review (Fitbit Charge 4, Garmin Vivosmart 4, WHOOP vs PSG; e52192)** — same pattern: duration-level metrics (TST) acceptable for healthy sleepers with proportional bias (overestimation), wake detection weak.
- **Garmin's own validation (Garmin Health, vs proprietary reference)** — sleep detection sensitivity 95.8%, wake specificity 73.4% (note: their specificity figure is not PSG-epoch-comparable to Chinoy's).

### Four-stage staging

- Garmin's own figure: **69.7% overall 4-class accuracy**; most common confusions deep→light and REM→light.
- Independent epoch analyses (device-comparison studies and the5krunner's aggregation of them) put real-world 4-stage agreement for Garmin nearer **40–55%**, versus ~60–80% for the best performers (recent Fitbit/WHOOP algorithms) in six-device validations (e.g. *Sleep Advances* 2025 six-device study — Garmin absent from the best tier; Fitbit/WHOOP reach stage kappa ~0.4–0.5, i.e. only moderate even at the top). Chance agreement for 4 classes with typical stage priors is ~35–40%, so Garmin staging sits barely above chance in independent tests.

### Disagreement to note

Garmin's published numbers are notably better than independent PSG comparisons; no peer-reviewed PSG validation of current-generation Garmin staging shows good (kappa > 0.6) agreement. Duration metrics also degrade in insomnia-like, fragmented sleep (specificity 0.18 means ~4 of 5 awake epochs are scored as sleep).

### Conclusion (Q-C)

"Trust duration, ignore stage minutes" is directionally right and is the correct system design, with one tightening: trust duration **for reasonably consolidated nights** while knowing Garmin overestimates TST (poor wake detection, specificity ~0.2), so treat reported TST as an upper bound on fragmented nights; do not surface or reason over deep/REM minutes at all — 4-stage agreement is ~50–70% at best and near chance in independent tests. Sleep duration and bedtime regularity are the only Garmin sleep fields defensible as coaching inputs.

---

## Q-D. HRV-guided training RCTs

### The trials and what HRV signal they actually used

- **Kiviniemi et al. (2007), *Eur J Appl Physiol* 101:743–751** — recreational runners. Decisions were **daily**, but not off a raw single reading in isolation: each morning's HF power was compared to an individual **10-day rolling reference band (mean − SD)**, with an additional 2-consecutive-day-decrease rule → low intensity/rest. HRV-guided group improved VO2max and max running velocity more than predefined training. (So the premise "weekly only" needs correcting for this trial: daily decision, multi-day smoothed baseline.)
- **Vesterinen et al. (2016), *Scand J Med Sci Sports*** — 40 recreational runners. **7-day rolling-averaged lnRMSSD vs an individual smallest-worthwhile-change band (±0.5 SD of baseline)**; within band → HIT allowed, outside → low intensity. HRV group improved 3000-m performance significantly with **fewer** high-intensity sessions.
- **Javaloyes et al. (2019), *Int J Sports Physiol Perform*** — 17 well-trained cyclists, 4 baseline weeks to establish resting HRV, then daily prescription driven by the **7-day rolling-averaged lnRMSSD relative to the SWC band**. HRV-guided group improved peak power (~5%) and 40-min TT power; traditional periodization group did not. Javaloyes et al. 2020 (runners) similar design and direction.
- **da Silva et al. (2019/2020)** — untrained women, HRV-guided running: primary reported benefits in mood, stress and recovery perception; performance differences small.
- **Meta-analyses:**
  - **Granero-Gallegos / Carrasco-Poyatos et al. (2020), *Int J Environ Res Public Health* 17:7999** — HRV-based training improved VO2max with a significantly larger effect than control training (small effect, moderated by athlete level and sex).
  - **Manresa-Rocamora et al. (2021), *Int J Environ Res Public Health* 18:10299** — methodological meta-analysis: SMD favoring HRV-guided but **non-significant**: VO2max +0.20 (95% CI −0.07 to 0.47), performance at VT2 +0.26 (−0.05 to 0.57), endurance performance +0.20 (−0.09 to 0.48). Also reported a lower rate of non-responders in HRV-guided groups. Honest read: HRV-guided is never worse, sometimes slightly better; the group-level margin is small and CI-crossing-zero.

### Confirmation of the premise

Confirmed with one correction: **every positive trial used a smoothed HRV signal compared against an individually established baseline band** — 7-day rolling average vs SWC in Vesterinen 2016 and Javaloyes 2019/2020, a 10-day reference band in Kiviniemi 2007. None gated training on a single morning reading in isolation, though Kiviniemi (and operationally all of them) applied the smoothed comparison **daily**, typically to swap that day's session intensity rather than to cancel training. The literature therefore supports rolling-average-vs-band logic; it does not require restricting decisions to once a week, but nothing in it supports hard daily go/no-go gating off one raw reading.

### Conclusion (Q-D)

"HRV weekly/rolling average vs an individual baseline band as weekly planning context, never a daily go/no-go gate" is well supported and is essentially the protocol the successful RCTs used (7-day rolling lnRMSSD vs ±0.5 SD SWC); expected benefit is modest at the group level (SMD ~0.2, often non-significant) but with a consistent direction and fewer non-responders, which fits its role as context rather than a primary driver. Implement it as: rolling 7-day lnRMSSD outside the baseline band → bias the coming days toward low intensity; inside the band → proceed as planned — and never cancel or green-light a session from one morning's number.
