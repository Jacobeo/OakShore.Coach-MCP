# How coach-mcp is used

Worked examples of the intended interaction, used to sanity-check the tool
surface. Terminology follows [CONTEXT.md](../CONTEXT.md).

## Tool surface

| Tool | Purpose |
| --- | --- |
| `get_athlete_profile` | Stable facts, plus any active Constraints |
| `update_athlete_profile` | Change weight, equipment, availability or prioritized Goals; add a Constraint |
| `get_sync_status` | Count stored records, report the last sync time per collection, and show the most recent Activity summary |
| `get_macrocycle` | The Macrocycle, its Mesocycles and WeekOutlines, and where today falls |
| `set_macrocycle` | Author or replace the Macrocycle |
| `get_training_summary` | Activities in a date range: counts by modality, volume, strength detail, Adherence |
| `get_current_form` | TrainingLoad, ACWR, TrainingReadiness, HRV, VO2max, sleep trend |
| `get_scheduled_workouts` | What is prescribed for a date range |
| `prescribe` | Write ScheduledWorkouts into the store for a date range |

Deferred: `push_to_garmin`, and `get_activity` for drilling into one session's
ExerciseSets.

Every response carries the time the underlying data was last synced, so the
agent can qualify its answer instead of reasoning confidently over a gap.

> **You:** Did the Activity sync land?
>
> **Agent:** The store has 15 Activities, last synced at 12:00 UTC. The most
> recent is a strength Activity from yesterday afternoon. The
> AthleteProfile has one record, last synced at 12:00 UTC.

## 1. Bootstrap: creating the AthleteProfile

The store is empty. The profile is *proposed from data*, not collected through a
form — most of it is already known.

> **You:** Let's set up my profile.

`get_athlete_profile` returns empty, so the agent reads what the sync already
has: body weight history, VO2max, and the shape of the last few months of
Activities.

> **Agent:** From your Garmin data I can fill in most of this. Body weight 115 kg,
> trending down about 0.8 kg a week. VO2max 38. Over the last 12 weeks you have
> trained 2.8 times a week on average, almost always Monday, Wednesday and either
> Friday or Saturday, typically 55–70 minutes. Activity types are mostly indoor
> cycling, strength training and walking.
>
> What I cannot infer, and need from you:
>
> - What are you training for, and when?
> - What equipment do you actually have access to?
> - Any injuries or limitations I should design around?
> - Is 3 sessions a week your intent, or just what happened?

> **You:** 7-a-side football season starts in April. That's my top Goal;
> improving VO2max is second. Full gym, plus a stationary
> bike at home. No access to a pitch over winter. Weak ankles and feet — that's
> the main thing. I'm on Wegovy so I'm losing weight. 3 sessions a week of about
> an hour is the plan.

`update_athlete_profile` writes it.

> **Agent:** Saved those Goals in priority order with a shared April deadline.
> I have flagged two things that will shape every prescription:
> preserving lean mass while on Wegovy, which means strength work is
> non-negotiable, and the ankles, which caps impact volume early on.

## 2. Bootstrap: importing an existing Macrocycle

The Macrocycle already exists as a document from an earlier AI conversation.

> **You:** *[pastes the six-month plan]* Here's my plan, can you load it?

> **Agent:** I found six Mesocycles. The document leaves the weekly build and
> recovery pattern open. Should November build for three weeks, then ease off?
>
> **You:** Yes. Use two cardio and one strength session in each build week, then
> one of each in the recovery week. Let's agree the other months the same way.

After discussing the remaining WeekOutlines, the model calls `set_macrocycle`.
The server stores the agreed structure and a snapshot of the AthleteProfile
Goals in priority order. The Macrocycle ends at their shared date; the server
never calls a model.

> **Agent:** Loaded as a Macrocycle with six Mesocycles:
>
> | Mesocycle | Dates | Adaptation |
> | --- | --- | --- |
> | Aerobic Base & Stability | Oct | Base endurance, joint stability |
> | Building Endurance | Nov | Volume, introduce fartlek |
> | Strength Emphasis | Dec | Heavier loads, maintain base |
> | Power & Intervals | Jan | Convert strength to power |
> | High-Intensity & Agility | Feb | Match-specific conditioning |
> | Peak & Taper | Mar | Peak, then taper into April |
>
> November's WeekOutlines build for three weeks, then reduce work for recovery:
> 2 cardio and 1 strength each build week; 1 cardio and 1 strength in week 4.
> I kept each week's progression and recovery focus in words, so later
> prescriptions can adjust without losing intent. Today falls before the
> Macrocycle starts, so there is nothing to prescribe yet.

Dates and each WeekOutline's modality counts are structured. Adaptation,
progression and recovery intent stay as prose for the model to reason over.

## 3. The weekly loop

> **You:** Plan my week.

One turn: `get_macrocycle`, `get_current_form`, `get_training_summary` for the last
14 days, `get_scheduled_workouts` for the next 7.

> **Agent:** You are in **Building Endurance**, WeekOutline 3 of 4. This is the
> last build week before a lighter recovery week. Data synced 6 hours ago.
>
> Last two weeks: 5 of 6 prescribed sessions done, one cardio day missed on the
> 8th. Your long ride reached 47 minutes against the 50–60 the Mesocycle asks for.
> Strength on track, and you added a set on squats.
>
> Form: chronic load 412, acute 380, ACWR 0.92 — absorbing the work comfortably.
> Readiness averaged 71. HRV inside baseline. Sleep averaged 6h40m, the weak spot.
>
> - **Mon** — Long ride, 55 min steady
> - **Wed** — Strength, full body, 3×10–12, plus ankle band work
> - **Sat** — Run with fartlek: 10 min easy, 4× (3 min moderate / 2 min easy), 5 cool-down
>
> Saved this week's Microcycle. The other days are for recovery.

> **You:** I took a knock to my knee on Friday and I've only got two evenings free.

`update_athlete_profile` records a Constraint with an expiry; `prescribe` rewrites
the week.

> **Agent:** Adjusted to two low-impact sessions, keeping the Mesocycle intent:
>
> - **Wed** — Bike 55 min steady. No impact, still serves the volume goal
> - **Sat** — Strength, leg press and seated work instead of squats and lunges.
>   Ankle band work stays; balance drills that load the knee are out
>
> Dropping the fartlek — running intervals on a fresh knee knock is how a niggle
> becomes six weeks off. If it has settled by next Sunday the intensity goes back.
>
> Scheduled 2 workouts, and noted the knee so it informs next week too.

## 4. From a phone

> **You:** What am I doing today?

`get_scheduled_workouts` for today. One call.

> **Agent:** Nothing today. Next is Wednesday: bike, 55 min steady. Knee
> Constraint active until the 24th, so keep it easy.

## 5. The question no Garmin tool can answer

> **You:** Where can I optimise to improve my VO2max?

`get_current_form`, `get_training_summary` for 90 days, `get_macrocycle`.

> **Agent:** VO2max flat at 38 for eleven weeks. Two things stand out.
>
> Your load focus is lopsided: 89% low aerobic, 11% high aerobic, 0% anaerobic.
> Garmin's own target range wants noticeably more high aerobic. You have the base;
> you are not touching the system that moves VO2max.
>
> And of 14 prescribed easy sessions in the last 90 days, 9 averaged heart rates
> in zone 3. You are training the middle at both ends — too hard to recover from,
> too easy to adapt to.
>
> Your Macrocycle already fixes the first part in January. The useful change now is
> making the easy sessions genuinely easy, so you arrive in January able to absorb
> the hard ones. Want me to add heart-rate ceilings to the easy prescriptions?

This last answer requires the Macrocycle (which Garmin does not have), the
Activities (which it does), and 90 days of both in one queryable place (which
only our own store provides). It is the reason this project exists rather than
running an existing Garmin MCP server.
