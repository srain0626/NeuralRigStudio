import { spawn } from "node:child_process";
import fs from "node:fs";
import net from "node:net";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const PIPE_PATH = "\\\\.\\pipe\\NeuralRigStudio_McpPipe_v1";
const __dirname = path.dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = path.resolve(__dirname, "..", "..", "..");
const PKG_VERSION = JSON.parse(fs.readFileSync(new URL("../package.json", import.meta.url), "utf8")).version;

export function resolveNativeMcpBinary(env = process.env, platform = process.platform) {
  if (env.NEURALRIG_MCP_FORCE_FALLBACK === "1") return null;

  const candidates = [];
  if (env.NEURALRIG_MCP_BIN) candidates.push(env.NEURALRIG_MCP_BIN);

  if (platform === "win32") {
    const localApp = env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local");
    const progFiles = env.ProgramFiles || "C:\\Program Files";
    candidates.push(
      path.join(localApp, "Programs", "NeuralRigStudio", "NeuralRigMcp.exe"),
      path.join(localApp, "NeuralRigStudio", "NeuralRigMcp.exe"),
      path.join(progFiles, "NeuralRigStudio", "NeuralRigMcp.exe"),
      path.join(REPO_ROOT, "bin", "Release", "net10.0-windows", "win-x64", "publish", "NeuralRigMcp.exe"),
      path.join(REPO_ROOT, "tools", "NeuralRigMcp", "bin", "Release", "net10.0-windows", "NeuralRigMcp.exe"),
      path.join(REPO_ROOT, "tools", "NeuralRigMcp", "bin", "Debug", "net10.0-windows", "NeuralRigMcp.exe")
    );
  }

  for (const c of candidates) {
    if (c && fs.existsSync(c)) return c;
  }
  return null;
}

export function probeLivePipe(timeoutMs = 700) {
  return new Promise((resolve) => {
    if (process.platform !== "win32") {
      resolve({ connected: false, reason: "Named pipe live bridge requires Windows NeuralRig Studio." });
      return;
    }
    let settled = false;
    const sock = net.createConnection(PIPE_PATH);
    const timer = setTimeout(() => {
      if (!settled) {
        settled = true;
        sock.destroy();
        resolve({ connected: false, reason: "timeout" });
      }
    }, timeoutMs);

    sock.on("connect", () => {
      sock.write(JSON.stringify({ cmd: "get_state" }) + "\n");
    });

    let buf = "";
    sock.on("data", (chunk) => {
      buf += chunk.toString("utf8");
      const nl = buf.indexOf("\n");
      if (nl !== -1 && !settled) {
        settled = true;
        clearTimeout(timer);
        sock.end();
        resolve({ connected: true, sample: buf.slice(0, nl).trim() });
      }
    });

    sock.on("error", (err) => {
      if (!settled) {
        settled = true;
        clearTimeout(timer);
        resolve({ connected: false, reason: err.code || err.message });
      }
    });
  });
}

function sendPipeCommand(cmd, argsObj = {}, timeoutMs = 4000) {
  return new Promise((resolve, reject) => {
    if (process.platform !== "win32") {
      reject(new Error("Live Studio pipe requires Windows NeuralRig Studio host."));
      return;
    }
    const sock = net.createConnection(PIPE_PATH);
    let buf = "";
    let done = false;
    const timer = setTimeout(() => {
      if (!done) {
        done = true;
        sock.destroy();
        reject(new Error("Timed out waiting for NeuralRig Studio live pipe."));
      }
    }, timeoutMs);

    sock.on("connect", () => {
      sock.write(JSON.stringify({ cmd, ...argsObj }) + "\n");
    });
    sock.on("data", (chunk) => {
      buf += chunk.toString("utf8");
      const nl = buf.indexOf("\n");
      if (nl !== -1 && !done) {
        done = true;
        clearTimeout(timer);
        sock.end();
        try {
          resolve(JSON.parse(buf.slice(0, nl)));
        } catch (e) {
          reject(e);
        }
      }
    });
    sock.on("error", (err) => {
      if (!done) {
        done = true;
        clearTimeout(timer);
        reject(err);
      }
    });
  });
}

const FALLBACK_TOOLS = [
  {
    name: "neuralrig_get_catalog",
    description: "List NeuralRig Studio built-in pedals, tube amps, cabinets, and custom SPICE/IR gear metadata.",
    inputSchema: { type: "object", properties: { query: { type: "string" }, category: { type: "string" } } },
  },
  {
    name: "neuralrig_get_state",
    description: "Get live NeuralRig Studio state (ASIO/WASAPI audio routing, active signal chain, scenes, presets, custom gear).",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "neuralrig_set_preset",
    description: "Apply a full 16-slot signal chain preset to the live NeuralRig Studio audio engine.",
    inputSchema: {
      type: "object",
      properties: {
        presetName: { type: "string" },
        routingMode: { type: "integer" },
        tempoBpm: { type: "number" },
        slots: { type: "array" },
      },
      required: ["slots"],
    },
  },
  {
    name: "neuralrig_get_spice_guide",
    description: "Return the MNA SPICE schematic netlist schema, verified research workflow, and semiconductor .MODEL requirements.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "neuralrig_simulate_spice",
    description: "Run offline/live MNA SPICE .OP/.AC/.TRAN verification on a schematic netlist (requires native NeuralRigMcp.exe on Windows).",
    inputSchema: { type: "object", properties: { circuit: { type: "object" } }, required: ["circuit"] },
  },
  {
    name: "neuralrig_commit_spice_device",
    description: "Register a verified SPICE circuit or acoustic cabinet IR directly into NeuralRig Studio.",
    inputSchema: { type: "object", properties: { id: { type: "string" }, name: { type: "string" }, category: { type: "string" } }, required: ["id", "name", "category"] },
  },
];

const FALLBACK_SPICE_GUIDE = {
  workflow: [
    "1. Call neuralrig_get_spice_guide to review SpiceCircuitDto and valid semiconductor models.",
    "2. Research the real schematic & datasheet values (e.g., 4.5V virtual bias, feedback RC poles, clippers). If no verifiable schematic exists, stop and inform the user.",
    "3. Register missing components via neuralrig_register_spice_model with datasheetUrl.",
    "4. Validate .OP, .AC, .TRAN harmonics, and 0 B RT allocation via neuralrig_simulate_spice.",
    "5. Register the validated gear with neuralrig_commit_spice_device so it appears in Signal Chain & Community Hub.",
  ],
  supportedElements: ["R", "C", "L", "POT (LIN|LOG|RLOG)", "SW", "D (Diode)", "Q (BJT N/P)", "J (JFET)", "M (MOSFET)", "U (OpAmp)", "T (Triode 12AX7/12AY7/12AT7)"],
  builtInModels: ["1N914", "1N4148", "1N34A", "1N270", "BAT41", "RED_LED", "GREEN_LED", "2N5088", "2N3904", "BC108", "AC128", "OC44", "J201", "2N5457", "2N7000", "BS170", "LM308", "LM741", "JRC4558", "TL072", "NE5532", "12AX7", "12AY7", "12AT7"],
};

const COMPACT_CATALOG_SUMMARY = [
  { id: "amp_plexi", name: "Brit Plexi 100 Bright", originalHardware: "Marshall 1959SLP Super Lead", category: "amp" },
  { id: "amp_2203", name: "Brit 800 Lead (2203)", originalHardware: "Marshall JCM800 2203", category: "amp" },
  { id: "amp_twin", name: "US Blackface Twin 85", originalHardware: "Fender '65 Twin Reverb", category: "amp" },
  { id: "amp_jc120", name: "JP Stereo Jazz 120", originalHardware: "Roland JC-120 Jazz Chorus", category: "amp" },
  { id: "amp_be100", name: "LA Brown Eye 100", originalHardware: "Friedman BE-100", category: "amp" },
  { id: "amp_5153", name: "Panama 53 Red Lead", originalHardware: "EVH 5150 III 100W Red", category: "amp" },
  { id: "od_ts808", name: "Green Screamer 808", originalHardware: "Ibanez TS808 Tube Screamer", category: "drive" },
  { id: "od_sd1", name: "Compact Yellow Super OD", originalHardware: "BOSS SD-1 Super OverDrive", category: "drive" },
  { id: "od_klon", name: "Mythic Gold Centaur", originalHardware: "Klon Centaur", category: "drive" },
  { id: "fuzz_face", name: "Round Smile Fuzz (Ge/Si)", originalHardware: "Dallas-Arbiter Fuzz Face (AC128/BC108)", category: "drive" },
  { id: "cab_v30", name: "4x12 Brit Vintage 30", originalHardware: "4x12 Celestion Vintage 30", category: "cab" },
  { id: "rev_bluesky", name: "Celestial Shimmer Reverb", originalHardware: "Strymon blueSky", category: "reverb" },
];

async function handleFallbackRpc(msg) {
  const { id, method, params } = msg;
  if (id === undefined || id === null) return null;

  const ok = (result) => ({ jsonrpc: "2.0", id, result });
  const err = (code, message) => ({ jsonrpc: "2.0", id, error: { code, message } });

  switch (method) {
    case "initialize":
      return ok({
        protocolVersion: params?.protocolVersion || "2024-11-05",
        capabilities: { tools: { listChanged: false }, resources: { subscribe: false, listChanged: false } },
        serverInfo: { name: "neuralrig-mcp-bridge", version: PKG_VERSION },
      });
    case "ping":
      return ok({});
    case "tools/list":
      return ok({ tools: FALLBACK_TOOLS });
    case "resources/list":
      return ok({ resources: [] });
    case "prompts/list":
      return ok({ prompts: [] });
    case "tools/call": {
      const name = params?.name || "";
      const args = params?.arguments || {};
      if (name === "neuralrig_get_spice_guide") {
        return ok({ content: [{ type: "text", text: JSON.stringify(FALLBACK_SPICE_GUIDE, null, 2) }] });
      }
      if (name === "neuralrig_get_catalog") {
        const q = String(args.query || "").toLowerCase();
        const cat = String(args.category || "all").toLowerCase();
        const items = COMPACT_CATALOG_SUMMARY.filter((d) =>
          (cat === "all" || d.category === cat) &&
          (!q || d.id.includes(q) || d.name.toLowerCase().includes(q) || d.originalHardware.toLowerCase().includes(q))
        );
        return ok({
          content: [{
            type: "text",
            text: JSON.stringify({
              mode: "portable-fallback",
              note: "Full 75+ catalog & SPICE solver activate automatically when NeuralRig Studio (Windows) is installed.",
              items,
            }, null, 2),
          }],
        });
      }
      if (name === "neuralrig_get_state" || name === "neuralrig_set_preset") {
        try {
          const live = await sendPipeCommand(name === "neuralrig_get_state" ? "get_state" : "set_preset", args);
          return ok({ content: [{ type: "text", text: JSON.stringify(live, null, 2) }] });
        } catch (pipeErr) {
          return ok({
            isError: true,
            content: [{
              type: "text",
              text: `NeuralRig Studio desktop engine is not reachable (${pipeErr.message}). Install & launch NeuralRig Studio on Windows or set NEURALRIG_MCP_BIN.`,
            }],
          });
        }
      }
      return ok({
        isError: true,
        content: [{
          type: "text",
          text: `Tool '${name}' requires the native NeuralRigMcp.exe DSP/SPICE engine. Run 'npx neuralrig-mcp doctor' to check installation path or set NEURALRIG_MCP_BIN.`,
        }],
      });
    }
    default:
      return err(-32601, `Method not found: ${method}`);
  }
}

export function startStdioServer() {
  const nativeBin = resolveNativeMcpBinary();
  if (nativeBin) {
    const child = spawn(nativeBin, [], { stdio: ["pipe", "pipe", "inherit"] });
    process.stdin.pipe(child.stdin);
    child.stdout.pipe(process.stdout);
    child.on("exit", (code) => process.exit(code ?? 0));
    process.on("SIGINT", () => child.kill("SIGINT"));
    process.on("SIGTERM", () => child.kill("SIGTERM"));
    return;
  }

  let buffer = Buffer.alloc(0);
  let preferContentLength = false;

  const emitFrame = (obj) => {
    if (!obj) return;
    const json = JSON.stringify(obj);
    if (preferContentLength) {
      const byteLen = Buffer.byteLength(json, "utf8");
      process.stdout.write(`Content-Length: ${byteLen}\r\n\r\n${json}`);
    } else {
      process.stdout.write(json + "\n");
    }
  };

  const pump = async () => {
    while (buffer.length > 0) {
      const text = buffer.toString("utf8");
      if (/^Content-Length:\s*\d+/i.test(text)) {
        preferContentLength = true;
        const headerEnd = buffer.indexOf("\r\n\r\n");
        if (headerEnd === -1) return;
        const header = buffer.slice(0, headerEnd).toString("ascii");
        const m = /Content-Length:\s*(\d+)/i.exec(header);
        if (!m) {
          buffer = buffer.slice(headerEnd + 4);
          continue;
        }
        const len = Number(m[1]);
        const total = headerEnd + 4 + len;
        if (buffer.length < total) return;
        const body = buffer.slice(headerEnd + 4, total).toString("utf8");
        buffer = buffer.slice(total);
        try {
          emitFrame(await handleFallbackRpc(JSON.parse(body)));
        } catch (e) {
          emitFrame({ jsonrpc: "2.0", id: null, error: { code: -32700, message: String(e) } });
        }
      } else {
        const nl = buffer.indexOf("\n");
        if (nl === -1) return;
        const line = buffer.slice(0, nl).toString("utf8").trim();
        buffer = buffer.slice(nl + 1);
        if (!line) continue;
        try {
          emitFrame(await handleFallbackRpc(JSON.parse(line)));
        } catch (e) {
          emitFrame({ jsonrpc: "2.0", id: null, error: { code: -32700, message: String(e) } });
        }
      }
    }
  };

  process.stdin.on("data", (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    void pump();
  });
}
