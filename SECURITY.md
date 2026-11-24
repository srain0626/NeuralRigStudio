# Security & Privacy Policy

## Supported Versions

| Version | Supported |
|:---|:---|
| `3.0.x` | Yes |
| `< 3.0.0` | No (upgrade via OTA or GitHub Releases) |

## Reporting a Vulnerability

Do **not** open a public GitHub issue for security vulnerabilities, OAuth token handling bugs, or arbitrary circuit/code execution vectors.

Instead, please contact **Ray Kim** privately via **GitHub Security Advisories** on `srain0626/NeuralRigStudio` or email **`raykim@users.noreply.github.com`** with:
- Affected component (`NeuralRig.Core`, `RigWebBridge`, `neuralrig-mcp`, `cloud/`, or installer)
- Minimal reproduction steps or proof-of-concept payload
- Impact assessment

You should expect an initial acknowledgment within 72 hours.

## Privacy & Automated Telemetry Guarantees

- **Log Scrubbing (`LogSanitizer`)**: Local OS usernames (`Environment.UserName`), machine hostnames (`Environment.MachineName`), `C:\Users\*` / `/home/*` directory paths, `Bearer` headers, and key-value secrets are stripped before logs are persisted in memory or attached to bug reports.
- **Community Gear Hub Verification**: Imported SPICE circuits and cabinet acoustic definitions undergo bounded `SpiceAnalyzer` verification (`MaxNodes = 36`, finite sample guard, `0 B` RT check) before registration.
- **Offline First**: Desktop audio operation never transmits telemetry unless the user signs in with Google or explicitly submits a bug report.
