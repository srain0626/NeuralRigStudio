import fs from "node:fs";
import os from "node:os";
import path from "node:path";

/**
 * Supported AI coding/assistant hosts and their MCP configuration locations.
 */
export function getClientSpecs(homeDir = os.homedir(), env = process.env, platform = process.platform) {
  const appData = env.APPDATA || path.join(homeDir, "AppData", "Roaming");

  const claudeDesktopPath =
    platform === "win32"
      ? path.join(appData, "Claude", "claude_desktop_config.json")
      : platform === "darwin"
        ? path.join(homeDir, "Library", "Application Support", "Claude", "claude_desktop_config.json")
        : path.join(homeDir, ".config", "Claude", "claude_desktop_config.json");

  return [
    {
      id: "claude-desktop",
      label: "Claude Desktop",
      format: "json",
      configPath: claudeDesktopPath,
      rootKey: "mcpServers",
    },
    {
      id: "claude-code",
      label: "Claude Code",
      format: "json",
      configPath: path.join(homeDir, ".claude.json"),
      rootKey: "mcpServers",
    },
    {
      id: "cursor",
      label: "Cursor",
      format: "json",
      configPath: path.join(homeDir, ".cursor", "mcp.json"),
      rootKey: "mcpServers",
    },
    {
      id: "windsurf",
      label: "Windsurf",
      format: "json",
      configPath: path.join(homeDir, ".codeium", "windsurf", "mcp_config.json"),
      rootKey: "mcpServers",
    },
    {
      id: "codex",
      label: "OpenAI Codex CLI",
      format: "toml",
      configPath: path.join(homeDir, ".codex", "config.toml"),
      rootKey: "mcp_servers",
    },
    {
      id: "gemini",
      label: "Gemini CLI",
      format: "json",
      configPath: path.join(homeDir, ".gemini", "settings.json"),
      rootKey: "mcpServers",
    },
    {
      id: "antigravity",
      label: "Google Antigravity",
      format: "json",
      configPath: path.join(homeDir, ".gemini", "antigravity", "mcp_config.json"),
      rootKey: "mcpServers",
    },
  ];
}

export function buildServerEntry(format = "json") {
  if (format === "toml") {
    return [
      "[mcp_servers.neuralrig]",
      'command = "npx"',
      'args = ["-y", "neuralrig-mcp"]',
    ].join("\n");
  }
  return {
    command: "npx",
    args: ["-y", "neuralrig-mcp"],
  };
}

export function mergeJsonMcpConfig(existingText, rootKey = "mcpServers", serverName = "neuralrig") {
  let obj = {};
  if (existingText && existingText.trim().length > 0) {
    obj = JSON.parse(existingText);
    if (!obj || typeof obj !== "object" || Array.isArray(obj)) {
      throw new Error("Existing JSON config root must be an object");
    }
  }
  const servers = obj[rootKey] && typeof obj[rootKey] === "object" && !Array.isArray(obj[rootKey])
    ? { ...obj[rootKey] }
    : {};
  servers[serverName] = buildServerEntry("json");
  const next = { ...obj, [rootKey]: servers };
  return JSON.stringify(next, null, 2) + "\n";
}

export function mergeTomlMcpConfig(existingText, serverName = "neuralrig") {
  const sectionHeader = `[mcp_servers.${serverName}]`;
  const block = [
    sectionHeader,
    'command = "npx"',
    'args = ["-y", "neuralrig-mcp"]',
  ].join("\n");

  const raw = existingText || "";
  const lines = raw.split(/\r?\n/);
  const startIdx = lines.findIndex((l) => l.trim() === sectionHeader);
  if (startIdx === -1) {
    const trimmed = raw.trimEnd();
    return (trimmed.length > 0 ? trimmed + "\n\n" : "") + block + "\n";
  }

  let endIdx = lines.length;
  for (let i = startIdx + 1; i < lines.length; i++) {
    const t = lines[i].trim();
    if (t.startsWith("[") && t.endsWith("]")) {
      endIdx = i;
      break;
    }
  }
  const updated = [...lines.slice(0, startIdx), block, ...lines.slice(endIdx)];
  return updated.join("\n").replace(/\n{3,}/g, "\n\n").trimEnd() + "\n";
}

export function installForClients({
  client = "all",
  dryRun = false,
  homeDir = os.homedir(),
  env = process.env,
  platform = process.platform,
}) {
  const specs = getClientSpecs(homeDir, env, platform);
  const targets =
    client === "all"
      ? specs
      : specs.filter((s) => s.id.toLowerCase() === client.toLowerCase());

  if (targets.length === 0) {
    const valid = specs.map((s) => s.id).join(", ");
    throw new Error(`Unknown client '${client}'. Valid options: all, ${valid}`);
  }

  const results = [];
  for (const spec of targets) {
    const existed = fs.existsSync(spec.configPath);
    const prevText = existed ? fs.readFileSync(spec.configPath, "utf8") : "";
    const nextText =
      spec.format === "toml"
        ? mergeTomlMcpConfig(prevText)
        : mergeJsonMcpConfig(prevText, spec.rootKey);

    if (!dryRun) {
      fs.mkdirSync(path.dirname(spec.configPath), { recursive: true });
      fs.writeFileSync(spec.configPath, nextText, "utf8");
    }

    results.push({
      id: spec.id,
      label: spec.label,
      configPath: spec.configPath,
      format: spec.format,
      existed,
      dryRun,
      status: dryRun ? "dry-run" : existed ? "updated" : "created",
    });
  }

  return results;
}
