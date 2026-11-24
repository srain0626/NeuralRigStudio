# neuralrig-mcp

Universal Model Context Protocol (MCP) server and multi-provider configurator for **NeuralRig Studio**.

## Quick Install across AI Clients

Configure all detected AI tools in one command:

```bash
npx -y neuralrig-mcp install --client all
```

Or target a specific AI provider:

```bash
npx -y neuralrig-mcp install --client claude-desktop
npx -y neuralrig-mcp install --client claude-code
npx -y neuralrig-mcp install --client cursor
npx -y neuralrig-mcp install --client windsurf
npx -y neuralrig-mcp install --client codex
npx -y neuralrig-mcp install --client gemini
npx -y neuralrig-mcp install --client antigravity
```

## Diagnostics

```bash
npx -y neuralrig-mcp doctor
```

Environment variables:

- `NEURALRIG_MCP_BIN`: Explicit path to `NeuralRigMcp.exe` if installed in a custom directory.
- `NEURALRIG_MCP_FORCE_FALLBACK=1`: Run the built-in portable JS MCP bridge without invoking `NeuralRigMcp.exe`.
