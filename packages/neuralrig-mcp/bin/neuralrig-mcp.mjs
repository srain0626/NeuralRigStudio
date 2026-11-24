#!/usr/bin/env node
import { buildServerEntry, getClientSpecs, installForClients } from "../src/clients.mjs";
import { probeLivePipe, resolveNativeMcpBinary, startStdioServer } from "../src/stdio-proxy.mjs";

const args = process.argv.slice(2);
const cmd = args[0];

function readFlag(name, defaultVal = undefined) {
  const idx = args.indexOf(name);
  if (idx !== -1 && idx + 1 < args.length) return args[idx + 1];
  const eq = args.find((a) => a.startsWith(`${name}=`));
  return eq ? eq.slice(name.length + 1) : defaultVal;
}

if (!cmd || cmd === "serve") {
  startStdioServer();
} else if (cmd === "--help" || cmd === "-h" || cmd === "help") {
  console.log(`neuralrig-mcp v3.0.0 — NeuralRig Studio Universal MCP Server & Multi-AI Installer

Usage:
  npx -y neuralrig-mcp
      Start MCP server over stdio (bridges to native NeuralRigMcp.exe or portable fallback).

  npx -y neuralrig-mcp install [--client <name>] [--dry-run]
      Configure AI coding tools automatically.
      Clients: all, claude-desktop, claude-code, cursor, windsurf, codex, gemini, antigravity

  npx -y neuralrig-mcp print-config [--client <name>]
      Print JSON/TOML snippet and target path without modifying files.

  npx -y neuralrig-mcp doctor
      Verify Node runtime, native NeuralRigMcp.exe path, and live Studio pipe connection.
`);
} else if (cmd === "--version" || cmd === "-v") {
  console.log("3.0.0");
} else if (cmd === "install") {
  const client = readFlag("--client", "all");
  const dryRun = args.includes("--dry-run");
  const results = installForClients({ client, dryRun });
  if (args.includes("--json")) {
    console.log(JSON.stringify({ ok: true, dryRun, results }, null, 2));
  } else {
    console.log(dryRun ? "[Dry Run] Planned MCP config updates:" : "Configured NeuralRig Studio MCP:");
    for (const r of results) {
      console.log(`  - ${r.label.padEnd(19)} [${r.status}] -> ${r.configPath}`);
    }
  }
} else if (cmd === "print-config") {
  const client = readFlag("--client", "all");
  const specs = getClientSpecs().filter((s) => client === "all" || s.id === client);
  for (const s of specs) {
    console.log(`# ${s.label} (${s.configPath})`);
    if (s.format === "toml") {
      console.log(buildServerEntry("toml") + "\n");
    } else {
      console.log(JSON.stringify({ [s.rootKey]: { neuralrig: buildServerEntry("json") } }, null, 2) + "\n");
    }
  }
} else if (cmd === "doctor") {
  const nativeBin = resolveNativeMcpBinary();
  const pipe = await probeLivePipe();
  console.log(
    JSON.stringify(
      {
        packageVersion: "3.0.0",
        nodeVersion: process.version,
        platform: process.platform,
        nativeMcpBinary: nativeBin || null,
        liveStudioPipe: pipe,
        supportedClients: getClientSpecs().map((s) => ({ id: s.id, path: s.configPath })),
      },
      null,
      2
    )
  );
} else {
  console.error(`Unknown subcommand '${cmd}'. Run 'neuralrig-mcp --help' for usage.`);
  process.exit(1);
}
