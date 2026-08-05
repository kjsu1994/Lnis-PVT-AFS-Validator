# Galileo E5 AltBOC Implementation Notes

Date: 2024-07-20

----

This note summarizes the current PocketSDR implementation for Galileo E5
wideband AltBOC tracking.

## Scope

The implemented signal is `E5ABQ`.

`E5ABQ` is a pilot-only AltBOC approximation that combines the Galileo E5aQ
and E5bQ pilot components at the E5 center frequency:

- RF frequency: `1191.795 MHz`
- Code period: `1 ms`
- Base code length: `10230 chips`
- Internal subcarrier grid: `40920` complex values per code period
- RINEX observation code mapping: `CODE_L8Q`

The design intentionally does not implement ICD-perfect full E5 AltBOC with
data-assisted product-term wipeoff. It uses only the two pilot sidebands, with
four-level integer subcarrier replicas.

## Signal Model

The replica is based on the AltBOC subcarriers defined in Galileo OS SIS ICD
Issue 2.1, section 2.1.1. The E5a and E5b pilot codes are multiplied by
complex single-sideband subcarrier tables sampled at four values per code
chip. The current integer tables are:

```text
E5a I:  11, -27,  27, -11, -11,  27, -27,  11
E5a Q: -27,  11,  11, -27,  27, -11, -11,  27
E5b I:  11, -27,  27, -11, -11,  27, -27,  11
E5b Q:  27, -11, -11,  27, -27,  11,  11, -27
```

The nominal amplitude scale is `SDR_ALTBOC_SCALE = 32`. This four-level
staircase replaced the earlier `-1/+1` square approximation and retains more
of the subcarrier shape while remaining compatible with the `int8_t` code
correlator.

Using the notation from the implementation:

- `SC_E5aI_I`, `SC_E5aI_Q` &nbsp;&nbsp; (sc_E5aI = sc_S - j*sc_P, lower sideband)
- `SC_E5bI_I`, `SC_E5bI_Q` &nbsp;&nbsp; (sc_E5bI = sc_S + j*sc_P, upper sideband)

The Q-channel subcarriers per ICD are 90 deg rotations of the corresponding
I-channel ones, with **opposite signs** between E5a and E5b:

```text
sc_E5aQ = +j * sc_E5aI
sc_E5bQ = -j * sc_E5bI
```

The ICD pilot Q-only signal is therefore

```text
S_Q(t) = c_E5aQ * sec_E5aQ * sc_E5aQ + c_E5bQ * sec_E5bQ * sc_E5bQ
       = j * [ c_E5aQ * sec_E5aQ * sc_E5aI - c_E5bQ * sec_E5bQ * sc_E5bI ]
```

Letting `rel = sec_E5aQ * sec_E5bQ`, a nominal matched local replica (after the
global `+j` is absorbed by carrier tracking) is

```text
R(t) = c_E5aQ * sc_E5aI - rel * c_E5bQ * sc_E5bI
```

The code expresses this more generally as

```text
R(t) = a(t) + p * sec_E5aQ * sec_E5bQ * b(t)
a(t) = c_E5aQ * sc_E5aI
b(t) = c_E5bQ * sc_E5bI
p in {-1, +1}
```

where the fixed relative polarity `p` is estimated from received correlations
instead of being assumed from table and phase conventions. Replica generation
is local to [src/sdr_ch.c](../src/sdr_ch.c); `sdr_gen_code("E5ABQ", ...)` and
`sdr_sec_code("E5ABQ", ...)` still return the E5aQ primary and secondary codes.
The E5bQ secondary code is stored separately in `ch->sec_code2`.

## Acquisition

Acquisition uses the E5aQ complex sideband replica only. It is generated on the
four-values-per-chip grid, resampled to the receiver sample rate, and converted
to the conjugate FFT used by the normal PCPS search. This avoids requiring
secondary-code alignment or relative-sideband-polarity calibration before
signal detection.

Once the signal is acquired, the channel starts tracking with the same E5aQ
AltBOC replica until the E5aQ secondary code is synchronized.

## Tracking

### Replica Banks

Tracking stores eight `int8_t` banks. Each bank has `SDR_N_CODES = 10`
fractional-code phases:

| Bank | Contents |
|---:|---|
| 0 | `aI`, real part of E5aQ sideband replica |
| 1 | `aQ`, imaginary part of E5aQ sideband replica |
| 2 | `bI`, real part of E5bQ sideband replica |
| 3 | `bQ`, imaginary part of E5bQ sideband replica |
| 4 | `pI = aI + bI` |
| 5 | `pQ = aQ + bQ` |
| 6 | `mI = aI - bI` |
| 7 | `mQ = aQ - bQ` |

The E5b banks are generated with an additional code offset of
`-sdr_e5ab_off`. The receiver option `-E5AB_OFF=<ns>` sets this E5b-minus-E5a
group-delay correction in nanoseconds. Prefix sums are generated for all banks
to remove the `+128` data bias used by the SIMD inner products.

The pre-combined values are not sign-limited. Since each sideband component is
bounded by `SDR_ALTBOC_SCALE`, their sums and differences remain within
`int8_t`. Separate-sideband correlations use scale 32 and are divided by two
when combined; pre-combined correlation uses scale 64, producing the same
normalization.

### Before Secondary-Code Synchronization

Only banks 0 and 1 are used. `sdr_corr_std()` mixes the carrier once and
computes the complex-code dot product

```text
corr = data * conj(code)
```

from the I and Q code banks over the shared IF samples. E5b is not included
until the E5aQ secondary-code epoch is known.

### Relative-Polarity Calibration

After E5aQ secondary-code synchronization, while `e5b_pol == 0`, the receiver
correlates the E5aQ and E5bQ sidebands separately. For each part of the prompt
correlation window, before and after the primary-code wrap, it accumulates a
normalized vote:

```text
dot   = Re(Ca * conj(Cb))
power = |Ca|^2 + |Cb|^2
score += 2 * sec_sign * dot / power
```

Here `sec_sign` is the E5aQ/E5bQ secondary-code product applicable to that
part of the window. During calibration, the sign of the running score selects
the provisional combination

```text
C = (Ca + p * sec_E5aQ * sec_E5bQ * Cb) / 2
```

After 64 tracking epochs, `e5b_pol` is fixed to `+1` or `-1` from the score.
This removes dependence on an assumed fixed relative sign between subcarrier,
code, and correlator conventions.

### Calibrated Fast Path

Once `e5b_pol` is known, the receiver selects either the pre-combined `a+b` or
`a-b` bank and calls `sdr_corr_std2()`. The carrier is mixed once, the I/Q
samples are loaded once, and both complex-code components are accumulated in
one correlation pass.

A correlation window can straddle the 1 ms primary-code boundary. The E5aQ and
E5bQ secondary-code products before and after that boundary can differ, so a
single bank plus a final polarity flip is insufficient. `sdr_corr_std2()`
therefore accepts one complex bank for the part before the wrap and another for
the part after it. The two choices are

```text
k1 = e5b_pol * sec_E5aQ[current] * sec_E5bQ[current]
k2 = e5b_pol * sec_E5aQ[next]    * sec_E5bQ[next]
bank1 = a+b if k1 > 0 else a-b
bank2 = a+b if k2 > 0 else a-b
```

The correlator then applies the E5aQ secondary-code transition between the two
window parts. This keeps the combined prompt continuous even when the code
offset places the wrap inside the integration interval.

The polarity calibration is cleared when secondary-code synchronization is
lost or when code-offset normalization changes the primary-code epoch. The
receiver then returns to the separate-sideband calibration path.

### Coherent Pilot PLL

`E5ABQ` is classified as a dataless pilot by `sdr_sig_pilot()`. After the FLL
pull-in and E5aQ secondary-code synchronization, the combined prompt is wiped
by the E5aQ secondary polarity and accumulated for
`K = max(1, round(sdr_t_coh / 1 ms))` periods. With the default
`sdr_t_coh = 20 ms`, the PLL uses a 20 ms complex prompt and a four-quadrant
`atan2` discriminator. PLI uses the same coherent intervals.

If secondary-code synchronization is lost, or if
`sdr_b_pll * K * 1 ms >= 0.4`, tracking falls back to the per-period Costas
PLL and clears the partial coherent sum.

### BOC Side-Peak Recovery

`sdr_sig_boc("E5ABQ")` returns true. When `bump_jump` is enabled, tracking adds
very-early and very-late correlators at `-1/3` and `+1/3` chip. A detected
side-peak lock moves the code offset by one-third chip before normal tracking
continues.

## Navigation Decoding

`E5ABQ` is routed through the existing E5aQ pilot navigation path. It is used as
a tracking and observation signal, not as an E5aI/E5bI data-channel decoder.

Data-channel demodulation remains the responsibility of the existing `E5AI` and
`E5BI` channels.

## Rejected Variants

### `E5IQ`

An earlier Tier 2 signal name, `E5IQ`, represented E5aQ-only AltBOC tracking.
It was useful for bring-up, but was removed from the public signal list after
`E5ABQ` became stable.

The E5aQ-only AltBOC replica still exists internally as the acquisition and
pre-secondary-sync fallback for `E5ABQ`.

### `E5ABQF`

An experimental full AltBOC-Q signal, `E5ABQF`, was also tested. It included
the E5 product terms and used four E5aI/E5bI data-sign hypotheses.

This was rejected because full AltBOC product terms require reliable wipeoff of
the E5aI and E5bI navigation data signs. Without data-assisted wipeoff, the
tracking loop sees unstable phase changes and split correlation peaks.

Supporting full AltBOC robustly would require sharing data-sign state from
separate E5AI/E5BI channels or adding a dedicated data-assisted wideband
tracking architecture. That is intentionally out of scope for the current
implementation.

## Implementation Checks

The correlator tests in [test_sdr_func.c](../test/src/test_sdr_func.c) cover the
two key equivalences used by this implementation:

- the fused complex-code path in `sdr_corr_std()` matches the scalar reference
  `data * conj(code)` calculation;
- one `sdr_corr_std2()` call with pre-combined `a+b`/`a-b` replicas matches two
  separate sideband correlations followed by `(Ca + k*Cb)/2`.

The latter is checked for both fixed relative polarities and all 16
combinations of the E5aQ/E5bQ secondary-code signs before and after the primary
code wrap. `test_sdr_perf.c` also benchmarks calibrated `E5ABQ` tracking at
24, 32, and 48 MHz sample rates.

## Expected Behavior

Compared with E5aQ or E5aI alone, `E5ABQ` can use both E5aQ and E5bQ pilot
power. The ideal pilot-power gain is about `3 dB` over a single pilot component.

In practice, the measured gain can be smaller because of:

- frontend passband ripple and roll-off,
- E5a/E5b gain imbalance,
- four-level subcarrier quantization,
- E5a/E5b group-delay error or a polarity-calibration error,
- receiver C/N0 estimator assumptions,
- finite front-end bandwidth.

The implementation should therefore be judged primarily by stable lock,
successful and repeatable `E5B POL` calibration, consistent code offset versus
E5a/E5b sideband channels, and a clean single correlation peak.
