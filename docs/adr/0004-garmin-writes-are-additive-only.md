# Writes to Garmin are additive and reversible only

The coach will eventually create workouts in Garmin and push them to the watch.
Writing through an unofficial API to a live account carries real risk, so the
write surface is deliberately constrained.

**Allowed**: creating a Workout, scheduling it to a date, pushing it to a device,
and the inverses of those (delete, unschedule). These only add future intentions.

**Forbidden**: `set_activity_exercise_sets`, and any other write against a
recorded Activity. That endpoint has replace-all semantics over real training
history, and a partial or malformed payload destroys data that cannot be
recovered from Garmin.

Exercise identifiers are validated against the client's bundled exercise catalog
before upload, because one unrecognised enum causes Garmin to reject the entire
payload atomically.

## Consequences

- Recorded history is permanently read-only. Corrections are made by hand in
  Garmin Connect, as they are today.
- The blast radius of a bug in the write path is a wrong workout on next week's
  calendar, never lost training history.
- A future contributor who notices we never fix up exercise sets programmatically
  should understand this is deliberate, not an oversight.
