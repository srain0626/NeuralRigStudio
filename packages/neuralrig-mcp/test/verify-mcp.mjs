import assert from "node:assert/strict";
import { execFileSync, spawn } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { installForClients, mergeJsonMcpConfig, mergeTomlMcpConfig } from "../src/clients.mjs";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const BIN = path.resolve(__dirname, "..", "bin", "neuralrig-mcp.mjs");

// 1) Test JSON & TOML client config mergers in an isolated temp home
const tmpHome = fs.mkdtempSync(path.join(os.tmpdir(), "nrig_mcp_test_"));
try {
  const results = installForClients({
    client: "all",
    dryRun: false,
    homeDir: tmpHome,
    env: { APPDATA: path.join(tmpHome, "AppData", "Roaming") },
    platform: "win32",
  });
  assert.equal(results.length, 7);
  for (const r of results) {
    assert.ok(fs.existsSync(r.configPath), `Missing generated config: ${r.configPath}`);
    const content = fs.readFileSync(r.configPath, "utf8");
    assert.ok(content.includes("neuralrig"), `Missing neuralrig entry in ${r.configPath}`);
  }

  // Verify idempotent TOML merge preserves unrelated tables
  const tomlMerged = mergeTomlMcpConfig('[model]\nname = "gpt-5"\n\n[mcp_servers.neuralrig]\ncommand = "old"\n');
  assert.ok(tomlMerged.includes('[model]\nname = "gpt-5"'));
  assert.ok(tomlMerged.includes('args = ["-y", "neuralrig-mcp"]'));
  assert.ok(!tomlMerged.includes('command = "old"'));

  // Verify JSON merge preserves existing servers
  const jsonMerged = mergeJsonMcpConfig('{"mcpServers":{"other":{"command":"echo"}}}');
  const parsed = JSON.parse(jsonMerged);
  assert.equal(parsed.mcpServers.other.command, "echo");
  assert.equal(parsed.mcpServers.neuralrig.command, "npx");

  console.log("[PASS] Client installer & JSON/TOML idempotent config merge (7 providers)");
} finally {
  fs.rmSync(tmpHome, { recursive: true, force: true });
}

// 2) Test CLI install --dry-run and doctor
const dryOut = execFileSync(process.execPath, [BIN, "install", "--client", "all", "--dry-run", "--json"], {
  encoding: "utf8",
});
const dryJson = JSON.parse(dryOut);
assert.equal(dryJson.ok, true);
assert.equal(dryJson.results.length, 7);

const doctorOut = execFileSync(process.execPath, [BIN, "doctor"], { encoding: "utf8" });
const doctorJson = JSON.parse(doctorOut);
assert.equal(doctorJson.packageVersion, "3.0.0");
console.log("[PASS] CLI install --dry-run & doctor verified");

// 3) Test Stdio JSON-RPC 2.0 protocol (portable fallback mode)
await new Promise((resolve, reject) => {
  const child = spawn(process.execPath, [BIN], {
    env: { ...process.env, NEURALRIG_MCP_FORCE_FALLBACK: "1" },
    stdio: ["pipe", "pipe", "pipe"],
  });
  let out = "";
  const replies = [];
  child.stdout.on("data", (d) => {
    out += d.toString("utf8");
    const lines = out.split("\n");
    out = lines.pop() ?? "";
    for (const line of lines) {
      if (line.trim()) replies.push(JSON.parse(line));
    }
    if (replies.length >= 4) {
      child.kill();
    }
  });
  child.on("close", () => {
    try {
      assert.equal(replies.length, 4);
      assert.equal(replies[0].result.serverInfo.name, "neuralrig-mcp-bridge");
      assert.ok(replies[1].result.tools.some((t) => t.name === "neuralrig_get_spice_guide"));
      const catText = replies[2].result.content[0].text;
      assert.ok(catText.includes("amp_plexi"));
      const guideText = replies[3].result.content[0].text;
      assert.ok(guideText.includes("neuralrig_simulate_spice"));
      console.log("[PASS] Stdio MCP JSON-RPC (initialize, tools/list, get_catalog, get_spice_guide)");
      resolve();
    } catch (e) {
      reject(e);
    }
  });
  child.on("error", reject);

  child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id: 1, method: "initialize", params: { protocolVersion: "2024-11-05" } }) + "\n");
  child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id: 2, method: "tools/list" }) + "\n");
  child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id: 3, method: "tools/call", params: { name: "neuralrig_get_catalog", arguments: { query: "plexi" } } }) + "\n");
  child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id: 4, method: "tools/call", params: { name: "neuralrig_get_spice_guide", arguments: {} } }) + "\n");
});
