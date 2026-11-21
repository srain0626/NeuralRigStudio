# Guided Guitar DI Profiler & Chromatic Tuner Guide

NeuralRig Studio includes two studio-grade audio analysis tools designed specifically for electric guitars:
1. **The Guided Guitar DI Profiler**: Calibrates your guitar's direct input (DI) signal for optimal preamp drive and dynamic response.
2. **The MPM Chromatic Tuner**: A precision chromatic tuner running on the McLeod Pitch Method with lock-free real-time audio muting.

---

## 1. Guided Guitar DI Profiler

### Why DI Calibration Matters
Different guitars produce drastically different output levels:
- A vintage Stratocaster single-coil pickup typically peaks around -20 to -14 dBFS with high 60 Hz hum.
- A modern active humbucker (e.g. EMG 81 or Fishman Fluence) can easily peak at -3 dBFS with almost zero idle hum.

Without calibration, an amp simulator will either sound overly thin and noisy or clip harshly before the amp stage. The Guitar Profiler eliminates this guesswork.

### 6-Step Calibration Wizard

```
[1. Silence] -> [2. Low E (E2)] -> [3. Open D (D3)] -> [4. Open B (B3)] -> [5. High E (E4)] -> [6. Strum]
```

1. **Step 1: Silence & Ambient Noise Floor**
   - **Action**: Mute all strings with your palm for 2 seconds.
   - **Analysis**: Calculates the true noise floor using a trimmed-mean bandpass filter (135 Hz to 2400 Hz) that deliberately separates guitar hum (60 Hz / 120 Hz / 180 Hz) from background thermal noise.
   - **Outcome**: Automatically calculates the ideal noise gate threshold (typically `noise_floor + 4.5 dB`).

2. **Steps 2 to 5: Individual Open Strings (E2, D3, B3, E4)**
   - **Action**: Pick the specified string once with normal medium strength and let it ring out.
   - **Analysis**:
     - Locks onto pitch using the McLeod Pitch Method (MPM) with a 4096-sample NSDF window.
     - Validates that the played note matches the target pitch within ±80 cents (rejects wrong notes or accidental harmonics).
     - Gathers attack transient peak, sustain decay rate, and spectral centroid.

3. **Step 6: Full Strum Test**
   - **Action**: Strum a full open chord (e.g. E Major or G Major) firmly.
   - **Analysis**:
     - Computes an 8192-point zero-padded FFT harmonic comb across the entire audio spectrum.
     - Measures maximum peak transient headroom (dBFS).
     - Evaluates harmonic richness (ratio of harmonic energy to fundamental energy).

### Profiler Output & Auto-Configuration

Upon completion, the Profiler saves your profile and can automatically apply:
- **Optimal Input Trim**: Sets the digital pre-gain so normal picking hits the amp model at the analog equivalent of 0 dBu to +4 dBu.
- **Smart Noise Gate**: Configures the initial noise gate to silence idle single-coil hum without prematurely killing chord decay.
- **Pickup Type Tag**: Classifies your instrument:
  - `Vintage Single-Coil` (High brightness, high hum, low-to-medium output)
  - `Modern Single-Coil / P90` (Warm mid bite, moderate output)
  - `Vintage Low-Output Humbucker` (Smooth top end, low hum, balanced output)
  - `High-Output Modern Humbucker` (High punch, ultra-low hum, high output)

---

## 2. Studio MPM Chromatic Tuner

### The McLeod Pitch Method (MPM)
Traditional zero-crossing or simple autocorrelation tuners often fail on electric guitar low notes (especially low E on neck pickups) because the 2nd harmonic (164.8 Hz) can be up to 12 dB louder than the fundamental (82.4 Hz).

NeuralRig Studio implements the **Normalized Square Difference Function (NSDF)**:
$$r_t(\tau) = \frac{2 \sum_{j} x_j x_{j+\tau}}{\sum_j x_j^2 + \sum_j x_{j+\tau}^2}$$

### Features
- **Accurate Fundamental Extraction**: 4096-sample analysis window with high-pass filtering (40 Hz cutoff) and sub-octave anti-jump logic guarantees steady detection down to Low E (and Drop D / Drop C).
- **Smooth 21-LED Display**: 21 segment visual bar with a centered green lock indicator and analog needle needle response.
- **Cent Deviation Readout**: Clear numeric cents display (-50.0 ct to +50.0 ct).
- **A4 Calibration Reference**: Adjustable reference frequency between 420 Hz and 460 Hz (standard 440 Hz).
- **Lock-Free Tuner Mute**: A real-time toggle that mutes the guitar signal without memory allocation or audio dropouts, perfect for stage and live streaming.
