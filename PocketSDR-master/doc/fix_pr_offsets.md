# Fix of Inter-Signal Pseudorange Offsets

2026-07-03

## 1. Problem (Issue #85)

When receiving multiple signals simultaneously, pseudoranges of different
signals of the same satellite at the same epoch showed integer-millisecond
offsets (1 or 2 ms, approx. 300 or 600 km). The offsets caused out-of-range
errors in RTCM3 MSM encoding. The offsets were stable within a receiver run
but changed between runs, so that one-time adjustments of the per-signal time
offset constants (TOFF_*) did not fix them permanently.

Long-run tests (11 h and 9 h logs) also revealed sporadic anomalies:
transient +2 ms / -66 ms / -74 ms excursions on QZSS L1S, +-72 ms on SBAS L5I,
a one-week (604800 s x c) pseudorange error on C01 B1I, and a constant +20 ms
offset on C58 B3I.

## 2. Investigation

Methods used:

- Offset statistics from $OBS/$CH logs over multi-hour runs, per signal,
  per PRN, per run.
- Deterministic replay of raw IF captures (`pocket_trk -toff <sec> <file>`),
  including sample-offset sweeps and time-shift tests, to separate
  signal-content effects from processing effects.
- Absolute pseudorange comparison against a co-located Septentrio mosaic-X5
  receiver (common GPS-C1C clock reference), to attribute errors to a specific
  signal chain.
- Temporary diagnostic logs at the TOW latch (decoded value, code offset,
  lock count, sync anchors, BCH error count).

Key experimental facts:

- The integer-ms offsets were PRN-independent, constant within a run
  (unchanged through 110 re-acquisitions), and flipped between runs.
- Replay of the same capture was bit-identical; shifting the file start by
  1..19 ms or 30/60 s did not change the offsets. The offsets were therefore a
  function of the received TOW value range, not of the processing alignment.
- The mosaic comparison showed the reference signals (L1CA, B1I, E1B, GLONASS)
  absolutely clean (0.0000 ms) while all signals whose TOFF constants had been
  shifted by -1 ms showed exactly +1.0000 ms (L2CM +2.0000 ms).

## 3. Root causes

### 3.1 FP truncation in the TOW latch (main cause)

`update_tow()` latched the channel TOW as `(int)(tow / 1e-3)` where
`tow = decoded_tow x N + TOFF_*`. The double rounding error of this sum
changes sign depending on the TOW magnitude (i.e. the day/time of the run)
and the TOFF fraction, so the truncation lands 1 ms low for some TOW ranges:

```
551123.119 / 1e-3 -> 551123118   (1 ms low)
  2576.220 / 1e-3 ->   2576219   (1 ms low)
255552.879 / 1e-3 ->    ...878   (1 ms low)
567132.879 / 1e-3 ->    ...879   (exact)
```

This explains run-dependence, PRN-independence, signal-type dependence and
occasional within-week flips.

### 3.2 Mis-calibrated TOFF constants

Commit 9d129c5 had shifted 8 TOFF constants by -1 ms (L2CM 0.880->0.879 etc.)
to cancel the truncation artifact observed on the calibration run. On runs
with different TOW ranges the shift itself became a +1 ms absolute error
(proven by the mosaic comparison). Structurally, each TOFF must be an integer
multiple of the signal's symbol period (decode fires on symbol boundaries),
which the pre-9d129c5 values were.

### 3.3 Unvalidated position-based TOW latch (SBAS format signals)

SBAS-format signals (QZSS L1S, SBAS L1CA/L5I, QZSS L5S) carry no TOW; the TOW
is latched from the message decode position plus a constant. The latch was
overwritten unconditionally every message, so symbol-stream slips under weak
signal caused wrong latches: +2 ms (1 symbol), or tens of ms which the 100 ms
ambiguity resolution wrapped into e.g. -66 ms.

### 3.4 Missing frame validation in the BDS D1/D2 decoder

`decode_D1D2NAV()` computed the BCH error count but never used it, and did
not check the subframe ID; a preamble match alone (11+11 bits) triggered
frame-sync re-anchoring, TOW latch and ephemeris storage. Corrupted frames
could silently inject garbage (observed as `EPHEMERIS UNMATCH`).

### 3.5 Coarse moduli in res_obs_amb()

Pilot signals resolved with 20/10/2 ms moduli inherited their own integer-ms
content, so slip errors of the pilot's position latch passed through.

### 3.6 Satellite-side effects (not receiver defects)

- C58 (BDS, non-operational/test satellite): its B3I D1 navigation frames are
  transmitted 20 ms (1 bit) offset from its B1I frames. Proven by two
  perfect (BCH-clean) latches 167.980 s apart in receiver time with SOW
  difference 168.000 s, and by the mosaic comparison (pocket C58 B1I
  0.0000 ms, B3I -20.0000 ms; mosaic itself consistent). Its ephemeris never
  passes validation. Commercial receivers are immune because they time B3I by
  code phase with the B1I-derived clock.
- GLONASS FDMA vs CDMA inter-signal code biases: per-SV constants
  (R12 16 m, R24 20 m; R06/R26 approx. 155-160 m), receiver-independent.
- SBAS L1 vs L5 inter-signal offsets up to approx. 537 m (1.8 us),
  provider-dependent.

These are real signal properties and remain in the raw observations.

## 4. Fixes

All in `src/sdr_nav.c`, `src/sdr_ch.c`, `src/sdr_pvt.c`:

1. `TOW_MS(t) = (int)floor((t) / 1e-3 + 0.5)` macro; used in `update_tow()`,
   in all 11 pilot latches `(int)(TOFF_* / 1e-3)`, in the SBAS latch, and in
   the per-cycle TOW increment of `sdr_ch.c`.
2. TOFF constants restored to structural values: L1CP 18.000, L2CM 0.880,
   L5I/L5Q 0.440, B1CP 14.000, B2AD 3.120, B2AP 0.900, B2BI 1.016.
3. SBAS-format re-latch gate: a new latch must be consistent with the
   propagated TOW modulo the 1 s message period, else the channel is
   unsynchronized (`$LOG ... TOW MISMATCH`). TOW mismatches of TOW-carrying
   signals are also logged.
4. BDS D1/D2 frame gate: frames with BCH error count > 5 or subframe ID
   outside 1..5 are rejected (`$LOG ... BDS D1D2 FRAME ERROR`). The initial
   TOW latch is logged for diagnosis (`$LOG ... D1D2 LATCH`).
5. `res_obs_amb()` moduli unified to 1 ms (the primary code period): only the
   sub-millisecond code phase is taken from the pilot, the integer part from
   the reference signal. SBAS L5I and QZSS L1S added as resolved signals.
6. Physical range gate in `gen_prng()`: pseudoranges outside 0.05..0.167 s
   are invalidated (`$LOG ... PSEUDORANGE OUT OF RANGE`); blocks week-error
   garbage from reaching RTCM3.
7. `res_obs_consist()`: per satellite, the integer-ms offset of every signal
   is aligned to the lowest-index valid signal each epoch. Absorbs
   satellite-side quirks (C58 B3I) and any residual slip; sub-ms content
   (real biases, ionosphere) is preserved. Alignments are logged at log
   level 4 (`$LOG ... PSEUDORANGE ALIGNED`).

## 5. Validation

- Replays of three multi-band raw IF captures (different days): all
  inter-signal integer-ms offsets are zero after the fixes (before: -2..0 ms
  varying per capture); no inconsistent pairs > 100 m remain.
- Mosaic absolute comparison confirmed the reference chains at 0.0000 ms.
- Re-latch gate verified to reject injected 2 ms slips in replay; no false
  rejections and no false alignments on clean data; observation availability
  unchanged.

## 6. Remaining items

- Bit/symbol sync was already made deterministic by the K-P bit-energy ML
  update (issue #83, see `doc/update_sym_sync.md`). Independently of that, a
  residual +-1 ms latch offset was observed on QZSS L1S between datasets
  (0 ms on a live run vs -1 ms on two capture replays, constant within each
  dataset). Its mechanism is not yet attributed; it is absorbed by
  `res_obs_consist()` and remains open for investigation.
- SBAS L1/L5 and GLONASS FDMA/CDMA inter-signal biases are real and remain in
  the raw observations (handle as DCB/ISB in processing).
- C58 is a non-operational test satellite; consider excluding PRN 58 from the
  signal configuration. Its B1I ephemeris never validates (harmless to PVT).
- L6DE / I1SD / SBAS TOFF constants not yet re-verified against structural
  values (no absolute reference data).
