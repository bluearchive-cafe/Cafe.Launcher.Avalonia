# Performance Audit

## Prioritize user-visible/high-volume costs

Look first at startup, UI responsiveness, whole-file IO, network/download paths, image decoding, repeated hashing, large collection rebuilds, serialization, retries, and resource lifetime.

## Inspect

- repeated full-file reads/hashes;
- repeated parsing/serialization;
- N+1 operations;
- nested scans over large collections;
- unnecessary allocations on hot paths;
- synchronous/blocking work in async flows;
- filesystem/network/image work on UI threads;
- cache invalidation that rebuilds unchanged state;
- repeated metadata syscalls in tight loops;
- retry loops multiplying expensive verification;
- undisposed streams/bitmaps/timers/events.

## Evidence rule

Prefer statements such as:

`The same file is read in two complete checksum passes with no intervening write.`

Do not claim `5–10× faster` or a precise millisecond improvement without measurement.

## Recommendations

For algorithm replacements or cache strategies, verify correctness and recommend benchmarking before final adoption. Avoid trading integrity/reliability for speed without explicit evidence.
