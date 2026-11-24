"use client";

import React, { useEffect, useState } from "react";
import {
  ALL_EIGHT_PATCHES,
  COPY,
  CURATED_HUB_FALLBACK,
  ChassisTheme,
  Locale,
  RIGBENCH_LEDGER,
} from "./LandingData";
import { CircuitAnatomyLab } from "./CircuitAnatomyLab";
import { OledSignalDeck } from "./OledSignalDeck";

export interface LiveReleaseSnapshot {
  version: string;
  installerUrl: string;
  standaloneExeUrl: string;
  releasePageUrl: string;
  sha256Installer?: string;
}

export interface LiveHubGearItem {
  id: string;
  name: string;
  originalName: string;
  category: string;
  ownerName: string;
  summary: string;
  downloads: number;
  tags: string[];
}

interface LandingExperienceProps {
  supabaseConfigured: boolean;
  repo: string;
  release: LiveReleaseSnapshot;
  liveHubItems: LiveHubGearItem[];
}

const AI_CLIENTS = [
  "Claude Desktop",
  "Claude Code",
  "Cursor",
  "Windsurf",
  "Codex CLI",
  "Gemini CLI",
  "Antigravity",
];

export function LandingExperience({
  supabaseConfigured,
  repo,
  release,
  liveHubItems,
}: LandingExperienceProps) {
  const [chassis, setChassis] = useState<ChassisTheme>("putty");
  const [locale, setLocale] = useState<Locale>("ko");
  const [activeBank, setActiveBank] = useState<string>("1C");
  const [selectedPatchRow, setSelectedPatchRow] = useState<string>("1C");
  const [copiedCmd, setCopiedCmd] = useState<string | null>(null);
  const [colophonOpen, setColophonOpen] = useState<boolean>(false);

  useEffect(() => {
    document.documentElement.setAttribute("data-chassis", chassis);
  }, [chassis]);

  useEffect(() => {
    document.documentElement.lang = locale === "ko" ? "ko" : "en";
  }, [locale]);

  const t = COPY[locale];

  const installerHref =
    release.installerUrl ||
    `https://github.com/${repo}/releases/download/v${release.version}/NeuralRigStudio-v${release.version}-Setup-win-x64.exe`;
  const standaloneHref =
    release.standaloneExeUrl ||
    `https://github.com/${repo}/releases/download/v${release.version}/NeuralRigStudio-v${release.version}-win-x64-standalone.exe`;

  const copyCommand = (cmd: string, key: string) => {
    navigator.clipboard?.writeText(cmd).catch(() => {});
    setCopiedCmd(key);
    window.setTimeout(() => {
      setCopiedCmd((prev) => (prev === key ? null : prev));
    }, 1800);
  };

  const handleLoadPatchToDeck = (bank: string, deckRef: string) => {
    setSelectedPatchRow(bank);
    setActiveBank(deckRef);
    const el = document.getElementById("deck");
    if (el) {
      el.scrollIntoView({ behavior: "smooth", block: "start" });
    }
  };

  const hubDisplay =
    liveHubItems.length > 0
      ? liveHubItems.slice(0, 3).map((g) => ({
          id: g.id,
          name: g.name,
          originalName: g.originalName,
          category: g.category,
          ownerName: g.ownerName,
          summary: g.summary,
          downloads: g.downloads,
          tags: g.tags.slice(0, 3),
        }))
      : CURATED_HUB_FALLBACK.map((g) => ({
          id: g.id,
          name: g.name,
          originalName: g.originalName,
          category: g.category,
          ownerName: g.ownerName,
          summary: locale === "ko" ? g.summaryKo : g.summaryEn,
          downloads: g.downloads,
          tags: g.tags,
        }));

  return (
    <div className="nr-viewport">
      {/* ====================================================================
          STICKY MACHINED UTILITY BAR
         ==================================================================== */}
      <header className="nr-topbar-wrap">
        <div className="nr-topbar">
          <a href="#deck" className="nr-brand">
            <span className="nr-brand-mark" aria-hidden="true">
              <svg width={16} height={16} viewBox="0 0 24 24" fill="none">
                <path
                  d="M2 12h4l2.6-7.5 3.8 15 2.6-7.5H22"
                  stroke="#ff4400"
                  strokeWidth={2.2}
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              </svg>
            </span>
            <span>NeuralRig Studio</span>
            <span className="nr-stamp">v{release.version}</span>
          </a>

          <nav className="nr-nav" aria-label="Primary sections">
            <a href="#deck">
              <span>01</span> {t.nav.deck}
            </a>
            <a href="#lab">
              <span>02</span> {t.nav.lab}
            </a>
            <a href="#matrix">
              <span>03</span> {t.nav.matrix}
            </a>
            <a href="#mcp">
              <span>04</span> {t.nav.mcp}
            </a>
            <a href="#bench">
              <span>05</span> {t.nav.bench}
            </a>
            <a href="#acquire">
              <span>06</span> {t.nav.download}
            </a>
          </nav>

          <div className="nr-controls">
            {/* Chassis Finish Switch */}
            <div className="nr-hw-switch" role="group" aria-label="Chassis surface finish">
              <button
                type="button"
                data-active={chassis === "putty"}
                onClick={() => setChassis("putty")}
                title="Putty Aluminum Finish"
              >
                <span
                  className="nr-swatch-dot"
                  style={{ background: "#dcd6c8", border: "1px solid #8c877b" }}
                />
                PUTTY
              </button>
              <button
                type="button"
                data-active={chassis === "carbon"}
                onClick={() => setChassis("carbon")}
                title="Carbon Darkroom Finish"
              >
                <span
                  className="nr-swatch-dot"
                  style={{ background: "#141413", border: "1px solid #57564f" }}
                />
                CARBON
              </button>
            </div>

            {/* KR / EN Editorial Switch */}
            <div className="nr-hw-switch" role="group" aria-label="Language">
              <button
                type="button"
                data-active={locale === "ko"}
                onClick={() => setLocale("ko")}
              >
                KR
              </button>
              <button
                type="button"
                data-active={locale === "en"}
                onClick={() => setLocale("en")}
              >
                EN
              </button>
            </div>
          </div>
        </div>
      </header>

      {/* ====================================================================
          ARCHITECTURAL MONOGRAPH SHEET
         ==================================================================== */}
      <main className="nr-sheet">
        {/* Top Calibration Spec Ribbon */}
        <div className="nr-spec-ribbon">
          {t.specRibbon.map((item) => (
            <div key={item.k} className="nr-spec-item">
              <span className="nr-spec-key">{item.k}:</span>
              <strong>{item.v}</strong>
            </div>
          ))}
        </div>

        {/* ==================================================================
            01 / HERO & INTERACTIVE RECESSED OLED 2x8 SIGNAL DECK
           ================================================================== */}
        <section id="deck" className="nr-section">
          <div className="nr-hero-grid">
            <div className="nr-hero-copy">
              <div>
                <div className="nr-index-tag">{t.hero.index}</div>
                <h1 className="nr-hero-title">
                  {t.hero.titleLine1}
                  <br />
                  <span className="nr-serif-accent" style={{ color: "var(--signal)" }}>
                    {t.hero.titleSerif}
                  </span>{" "}
                  {t.hero.titleLine2}
                </h1>
                <p className="nr-hero-lead">{t.hero.lead}</p>

                <div className="nr-cta-row" style={{ marginTop: 28 }}>
                  <a href={installerHref} className="nr-btn-primary">
                    <div>
                      <div>{t.hero.primaryCta}</div>
                      <div className="nr-btn-sub">{t.hero.primarySub}</div>
                    </div>
                    <span style={{ fontFamily: "var(--font-mono)", fontSize: 16 }}>↓</span>
                  </a>

                  <a href={standaloneHref} className="nr-btn-secondary">
                    <div>
                      <div>{t.hero.secondaryCta}</div>
                      <div className="nr-btn-sub" style={{ color: "var(--ink-3)" }}>
                        {t.hero.secondarySub}
                      </div>
                    </div>
                    <span style={{ fontFamily: "var(--font-mono)", fontSize: 14 }}>↗</span>
                  </a>
                </div>
              </div>

              <div className="nr-micro-metrics">
                {t.hero.metrics.map((m) => (
                  <div key={m.lbl}>
                    <div className="nr-metric-val">{m.val}</div>
                    <div className="nr-metric-lbl">{m.lbl}</div>
                  </div>
                ))}
              </div>
            </div>

            <div className="nr-hero-stage">
              <OledSignalDeck
                locale={locale}
                activeBank={activeBank}
                onSelectBank={(b) => {
                  setActiveBank(b);
                  setSelectedPatchRow(b);
                }}
              />
              <div
                style={{
                  marginTop: 10,
                  display: "flex",
                  justifyContent: "space-between",
                  alignItems: "center",
                  flexWrap: "wrap",
                  gap: 8,
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  color: "var(--ink-3)",
                }}
              >
                <span>
                  {locale === "ko"
                    ? "* 노브를 위아래로 드래그하거나 [톤 오디션]을 눌러 실시간 배음 변화를 확인하세요."
                    : "* Drag rotary pots vertically or press [AUDITION] to hear real-time harmonic shaping."}
                </span>
                <span>FIG 1.1 — LIVE WEB SIMULATION</span>
              </div>
            </div>
          </div>
        </section>

        {/* ==================================================================
            02 / ANATOMY OF PHYSICAL SOUND (BODE & HARMONIC LAB)
           ================================================================== */}
        <section id="lab" className="nr-section">
          <CircuitAnatomyLab locale={locale} />
        </section>

        {/* ==================================================================
            03 / UNIVERSAL AUDIO ROUTER & FACTORY PATCH LINER SHEET
           ================================================================== */}
        <section id="matrix" className="nr-section">
          <div className="nr-sec-head">
            <div className="nr-sec-head-left">
              <div className="nr-index-tag">{t.matrix.index}</div>
              <h2 className="nr-sec-title">
                {t.matrix.titlePre}{" "}
                <span className="nr-serif-accent" style={{ color: "var(--signal)" }}>
                  {t.matrix.titleSerif}
                </span>{" "}
                {t.matrix.titlePost}
              </h2>
            </div>
            <div className="nr-sec-head-right">
              <p className="nr-sec-desc">{t.matrix.desc}</p>
            </div>
          </div>

          <div className="nr-split-2">
            {/* Left: 4 Hardware Clock Topologies */}
            <div className="nr-col-left">
              <div
                style={{
                  padding: "12px 28px",
                  borderBottom: "1px solid var(--hairline)",
                  background: "var(--bg-recessed)",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  fontWeight: 600,
                  letterSpacing: "0.06em",
                  color: "var(--ink-3)",
                }}
              >
                {t.matrix.topoHeading}
              </div>

              {t.matrix.topologies.map((tp) => (
                <div key={tp.code} className="nr-topo-card">
                  <div
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 10.5,
                      fontWeight: 600,
                      letterSpacing: "0.05em",
                      color: "var(--signal)",
                    }}
                  >
                    {tp.code}
                  </div>
                  <h3
                    style={{
                      margin: "6px 0 0",
                      fontSize: 15.5,
                      fontWeight: 700,
                      letterSpacing: "-0.015em",
                    }}
                  >
                    {tp.title}
                  </h3>
                  <p
                    style={{
                      margin: "6px 0 0",
                      fontSize: 13.5,
                      lineHeight: 1.6,
                      color: "var(--ink-2)",
                    }}
                  >
                    {tp.body}
                  </p>
                </div>
              ))}
            </div>

            {/* Right: 8-Bank Vinyl Liner-Note Patch Table */}
            <div>
              <div
                style={{
                  padding: "12px 28px",
                  borderBottom: "1px solid var(--hairline)",
                  background: "var(--bg-recessed)",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  fontWeight: 600,
                  letterSpacing: "0.06em",
                  color: "var(--ink-3)",
                  display: "flex",
                  justifyContent: "space-between",
                }}
              >
                <span>{t.matrix.patchHeading}</span>
                <span>BANKS 1A — 2D</span>
              </div>

              {ALL_EIGHT_PATCHES.map((p) => {
                const isSel = selectedPatchRow === p.bank;
                return (
                  <button
                    key={p.bank}
                    type="button"
                    className="nr-patch-row"
                    data-selected={isSel}
                    onClick={() => handleLoadPatchToDeck(p.bank, p.deckRef)}
                  >
                    <span
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 12,
                        fontWeight: 600,
                        padding: "3px 7px",
                        border: `1px solid ${isSel ? "var(--signal)" : "var(--hairline)"}`,
                        color: isSel ? "var(--signal)" : "var(--ink-2)",
                        borderRadius: 2,
                        textAlign: "center",
                      }}
                    >
                      {p.bank}
                    </span>

                    <strong style={{ fontSize: 14, letterSpacing: "-0.015em" }}>
                      {p.title}
                    </strong>

                    <span
                      className="nr-patch-chain-cell"
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 11.5,
                        color: "var(--ink-2)",
                      }}
                    >
                      {p.chain}
                    </span>

                    <span
                      className="nr-patch-tag-cell"
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 10.5,
                        color: isSel ? "var(--signal)" : "var(--ink-3)",
                        whiteSpace: "nowrap",
                      }}
                    >
                      {isSel
                        ? t.matrix.loadToDeck
                        : locale === "ko"
                          ? p.tagKo
                          : p.tagEn}
                    </span>
                  </button>
                );
              })}
            </div>
          </div>
        </section>

        {/* ==================================================================
            04 / SCHEMATIC TO SILICON (NPX NEURALRIG-MCP BLUEPRINT)
           ================================================================== */}
        <section id="mcp" className="nr-section">
          <div className="nr-sec-head">
            <div className="nr-sec-head-left">
              <div className="nr-index-tag">{t.mcp.index}</div>
              <h2 className="nr-sec-title">
                {t.mcp.titlePre}{" "}
                <span className="nr-serif-accent" style={{ color: "var(--signal)" }}>
                  {t.mcp.titleSerif}
                </span>
                {t.mcp.titlePost ? ` ${t.mcp.titlePost}` : ""}
              </h2>
            </div>
            <div className="nr-sec-head-right">
              <p className="nr-sec-desc">{t.mcp.desc}</p>
            </div>
          </div>

          {/* Recessed Command Strip */}
          <div
            style={{
              padding: "26px 32px",
              background: "var(--oled-bg)",
              color: "var(--oled-ink)",
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(300px, 1fr))",
              gap: 18,
            }}
          >
            <div
              style={{
                padding: "16px 18px",
                background: "#121412",
                border: "1px solid #262925",
                borderRadius: 2,
              }}
            >
              <div
                style={{
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  color: "#8a887f",
                  marginBottom: 8,
                }}
              >
                {t.mcp.installLabel}
              </div>
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 12,
                }}
              >
                <code
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 13.5,
                    color: "#f2efe8",
                  }}
                >
                  <span style={{ color: "#ff4400" }}>$</span> npx -y neuralrig-mcp install --client all
                </code>
                <button
                  type="button"
                  onClick={() => copyCommand("npx -y neuralrig-mcp install --client all", "install")}
                  style={{
                    border: "1px solid #353934",
                    background: copiedCmd === "install" ? "#ff4400" : "#1b1e1b",
                    color: "#ffffff",
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    fontWeight: 600,
                    padding: "5px 10px",
                    borderRadius: 2,
                    cursor: "pointer",
                    flexShrink: 0,
                  }}
                >
                  {copiedCmd === "install" ? t.mcp.copied : t.mcp.copy}
                </button>
              </div>
            </div>

            <div
              style={{
                padding: "16px 18px",
                background: "#121412",
                border: "1px solid #262925",
                borderRadius: 2,
                display: "flex",
                flexDirection: "column",
                justifyContent: "space-between",
                gap: 10,
              }}
            >
              <div
                style={{
                  display: "flex",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 12,
                }}
              >
                <code
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 13,
                    color: "#d6d3ca",
                  }}
                >
                  <span style={{ color: "#22c55e" }}>$</span> npx -y neuralrig-mcp doctor
                </code>
                <button
                  type="button"
                  onClick={() => copyCommand("npx -y neuralrig-mcp doctor", "doctor")}
                  style={{
                    border: "1px solid #353934",
                    background: copiedCmd === "doctor" ? "#22c55e" : "#1b1e1b",
                    color: "#ffffff",
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    fontWeight: 600,
                    padding: "5px 10px",
                    borderRadius: 2,
                    cursor: "pointer",
                    flexShrink: 0,
                  }}
                >
                  {copiedCmd === "doctor" ? t.mcp.copied : t.mcp.copy}
                </button>
              </div>

              <div style={{ display: "flex", flexWrap: "wrap", gap: 6 }}>
                {AI_CLIENTS.map((client) => (
                  <span
                    key={client}
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 10,
                      padding: "2px 6px",
                      border: "1px solid #292d28",
                      color: "#9c9a91",
                      borderRadius: 2,
                    }}
                  >
                    {client}
                  </span>
                ))}
              </div>
            </div>
          </div>

          {/* 5-Step Blueprint Pipeline */}
          <div className="nr-mcp-steps">
            {t.mcp.steps.map((st) => (
              <div key={st.num} className="nr-mcp-step">
                <div>
                  <div
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 10.5,
                      fontWeight: 600,
                      color: "var(--signal)",
                      letterSpacing: "0.06em",
                    }}
                  >
                    {st.num}
                  </div>
                  <h3
                    style={{
                      margin: "8px 0 0",
                      fontSize: 15,
                      fontWeight: 700,
                      letterSpacing: "-0.015em",
                    }}
                  >
                    {st.title}
                  </h3>
                  <p
                    style={{
                      margin: "8px 0 0",
                      fontSize: 13,
                      lineHeight: 1.6,
                      color: "var(--ink-2)",
                    }}
                  >
                    {st.body}
                  </p>
                </div>

                <div
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    color: "var(--ink-3)",
                    paddingTop: 10,
                    borderTop: "1px dashed var(--hairline)",
                    overflow: "hidden",
                    textOverflow: "ellipsis",
                  }}
                >
                  {st.cmd}
                </div>
              </div>
            ))}
          </div>
        </section>

        {/* ==================================================================
            05 / RIGBENCH CALIBRATION LEDGER & COMMUNITY GEAR HUB
           ================================================================== */}
        <section id="bench" className="nr-section">
          <div className="nr-sec-head">
            <div className="nr-sec-head-left">
              <div className="nr-index-tag">{t.bench.index}</div>
              <h2 className="nr-sec-title">
                {t.bench.titlePre}{" "}
                <span className="nr-serif-accent" style={{ color: "var(--signal)" }}>
                  {t.bench.titleSerif}
                </span>{" "}
                {t.bench.titlePost}
              </h2>
            </div>
            <div className="nr-sec-head-right">
              <p className="nr-sec-desc">{t.bench.desc}</p>
            </div>
          </div>

          <div className="nr-split-bench">
            {/* Left: Calibration Ledger */}
            <div className="nr-col-left" style={{ display: "flex", flexDirection: "column" }}>
              <div
                style={{
                  padding: "12px 18px",
                  borderBottom: "1px solid var(--hairline)",
                  background: "var(--bg-recessed)",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  fontWeight: 600,
                  letterSpacing: "0.06em",
                  color: "var(--ink-3)",
                }}
              >
                {t.bench.certTitle}
              </div>

              <div style={{ overflowX: "auto" }}>
                <table className="nr-calib-table">
                  <thead>
                    <tr>
                      <th>{t.bench.colTopology}</th>
                      <th>{t.bench.colBudget}</th>
                      <th>{t.bench.colMeasured}</th>
                      <th>{t.bench.colUsage}</th>
                      <th>{t.bench.colAlloc}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {RIGBENCH_LEDGER.map((row) => (
                      <tr key={row.testEn}>
                        <td style={{ fontWeight: 600 }}>
                          {locale === "ko" ? row.testKo : row.testEn}
                        </td>
                        <td
                          style={{
                            fontFamily: "var(--font-mono)",
                            color: "var(--ink-3)",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {row.budget}
                        </td>
                        <td
                          style={{
                            fontFamily: "var(--font-mono)",
                            fontWeight: 600,
                            whiteSpace: "nowrap",
                          }}
                        >
                          {row.measured}
                        </td>
                        <td
                          style={{
                            fontFamily: "var(--font-mono)",
                            color: "var(--ink-2)",
                            whiteSpace: "nowrap",
                          }}
                        >
                          {row.headroom}
                        </td>
                        <td style={{ whiteSpace: "nowrap" }}>
                          <span
                            style={{
                              display: "inline-block",
                              whiteSpace: "nowrap",
                              fontFamily: "var(--font-mono)",
                              fontSize: 11,
                              fontWeight: 600,
                              padding: "2px 7px",
                              background: "rgba(34, 197, 94, 0.14)",
                              color: "#16a34a",
                              border: "1px solid rgba(34, 197, 94, 0.35)",
                              borderRadius: 2,
                            }}
                          >
                            {row.alloc}
                          </span>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div
                style={{
                  marginTop: "auto",
                  padding: "18px 20px",
                  background: "var(--bg-elevated)",
                  borderTop: "1px solid var(--hairline)",
                  display: "flex",
                  flexWrap: "wrap",
                  alignItems: "center",
                  justifyContent: "space-between",
                  gap: 12,
                  fontFamily: "var(--font-mono)",
                  fontSize: 11,
                }}
              >
                <span>
                  NAM WAVENET FEATHER (64fr vs 256fr INVARIANCE): <strong>ESR 0.00E+000 (100%)</strong>
                </span>
                <span style={{ color: "var(--signal)", fontWeight: 600 }}>
                  CALIBRATED · SUITE AVG 98.2%
                </span>
              </div>
            </div>

            {/* Right: Community Gear Hub Dispatch */}
            <div>
              <div
                style={{
                  padding: "12px 24px",
                  borderBottom: "1px solid var(--hairline)",
                  background: "var(--bg-recessed)",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  fontWeight: 600,
                  letterSpacing: "0.06em",
                  color: "var(--ink-3)",
                  display: "flex",
                  justifyContent: "space-between",
                }}
              >
                <span>{t.bench.hubTitle}</span>
                <span>{liveHubItems.length > 0 ? "LIVE SUPABASE" : "CURATED"}</span>
              </div>

              <div
                style={{
                  padding: "12px 24px",
                  borderBottom: "1px solid var(--hairline)",
                  fontSize: 12.5,
                  color: "var(--ink-2)",
                }}
              >
                {t.bench.hubSub}
              </div>

              {hubDisplay.map((item) => (
                <div
                  key={item.id}
                  style={{
                    padding: "18px 24px",
                    borderBottom: "1px solid var(--hairline)",
                  }}
                >
                  <div
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: 8,
                    }}
                  >
                    <strong style={{ fontSize: 14.5 }}>{item.name}</strong>
                    <span
                      style={{
                        fontFamily: "var(--font-mono)",
                        fontSize: 10,
                        padding: "2px 6px",
                        border: "1px solid var(--hairline)",
                        color: "var(--ink-2)",
                        textTransform: "uppercase",
                      }}
                    >
                      {item.category} · ↓ {item.downloads}
                    </span>
                  </div>
                  <div
                    style={{
                      fontFamily: "var(--font-mono)",
                      fontSize: 11,
                      color: "var(--ink-3)",
                      marginTop: 2,
                    }}
                  >
                    Ref: {item.originalName} · by {item.ownerName}
                  </div>
                  <p
                    style={{
                      margin: "8px 0 0",
                      fontSize: 13,
                      lineHeight: 1.55,
                      color: "var(--ink-2)",
                    }}
                  >
                    {item.summary}
                  </p>
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 6, marginTop: 10 }}>
                    {item.tags.map((tag) => (
                      <span
                        key={tag}
                        style={{
                          fontFamily: "var(--font-mono)",
                          fontSize: 10,
                          color: "var(--ink-3)",
                          background: "var(--bg-recessed)",
                          padding: "2px 6px",
                          borderRadius: 2,
                        }}
                      >
                        {tag}
                      </span>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>
        </section>

        {/* ==================================================================
            06 / ACQUIRE RELEASE NAMEPLATE & SYSTEM TELEMETRY COLOPHON
           ================================================================== */}
        <section id="acquire" className="nr-section" style={{ borderBottom: 0 }}>
          <div className="nr-sec-head">
            <div className="nr-sec-head-left">
              <div className="nr-index-tag">{t.acquire.index}</div>
              <h2 className="nr-sec-title">
                {t.acquire.titlePre}{" "}
                <span className="nr-serif-accent" style={{ color: "var(--signal)" }}>
                  {t.acquire.titleSerif}
                </span>
              </h2>
            </div>
            <div className="nr-sec-head-right">
              <p className="nr-sec-desc">{t.acquire.desc}</p>
            </div>
          </div>

          {/* Dual Machined Release Cards */}
          <div
            style={{
              display: "grid",
              gridTemplateColumns: "repeat(auto-fit, minmax(300px, 1fr))",
              borderBottom: "1px solid var(--hairline)",
            }}
          >
            <div
              style={{
                padding: "32px",
                borderRight: "1px solid var(--hairline)",
                display: "flex",
                flexDirection: "column",
                justifyContent: "space-between",
                gap: 22,
                background: "var(--bg-elevated)",
              }}
            >
              <div>
                <div
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    color: "var(--signal)",
                    fontWeight: 600,
                    letterSpacing: "0.06em",
                  }}
                >
                  OPTION 01 · RECOMMENDED FOR DESKTOP + MCP
                </div>
                <h3 style={{ margin: "8px 0 0", fontSize: 20, fontWeight: 700 }}>
                  {t.acquire.installerCardTitle}
                </h3>
                <p style={{ margin: "8px 0 0", fontSize: 13.5, lineHeight: 1.6, color: "var(--ink-2)" }}>
                  {t.acquire.installerCardBody}
                </p>
              </div>

              <a href={installerHref} className="nr-btn-primary">
                <span>NeuralRigStudio-v{release.version}-Setup-win-x64.exe</span>
                <span style={{ fontFamily: "var(--font-mono)" }}>↓</span>
              </a>
            </div>

            <div
              style={{
                padding: "32px",
                display: "flex",
                flexDirection: "column",
                justifyContent: "space-between",
                gap: 22,
              }}
            >
              <div>
                <div
                  style={{
                    fontFamily: "var(--font-mono)",
                    fontSize: 10.5,
                    color: "var(--ink-3)",
                    fontWeight: 600,
                    letterSpacing: "0.06em",
                  }}
                >
                  OPTION 02 · ZERO-FOOTPRINT SINGLE BINARY
                </div>
                <h3 style={{ margin: "8px 0 0", fontSize: 20, fontWeight: 700 }}>
                  {t.acquire.standaloneCardTitle}
                </h3>
                <p style={{ margin: "8px 0 0", fontSize: 13.5, lineHeight: 1.6, color: "var(--ink-2)" }}>
                  {t.acquire.standaloneCardBody}
                </p>
              </div>

              <a href={standaloneHref} className="nr-btn-secondary">
                <span>NeuralRigStudio-v{release.version}-win-x64-standalone.exe</span>
                <span style={{ fontFamily: "var(--font-mono)" }}>↓</span>
              </a>
            </div>
          </div>

          {/* Footer Colophon & Expandable Cloud API Gateway Status */}
          <footer style={{ padding: "22px 32px", background: "var(--bg-recessed)" }}>
            <div
              style={{
                display: "flex",
                flexWrap: "wrap",
                alignItems: "center",
                justifyContent: "space-between",
                gap: 16,
                fontFamily: "var(--font-mono)",
                fontSize: 11.5,
                color: "var(--ink-2)",
              }}
            >
              <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center", gap: 16 }}>
                <strong>NEURALRIG STUDIO · MIT LICENSE</strong>
                <span>Crafted by Ray Kim</span>
                <a
                  href={`https://github.com/${repo}`}
                  target="_blank"
                  rel="noreferrer"
                  style={{ textDecoration: "underline", textUnderlineOffset: 3 }}
                >
                  GitHub ({repo}) ↗
                </a>
                <a
                  href={release.releasePageUrl}
                  target="_blank"
                  rel="noreferrer"
                  style={{ textDecoration: "underline", textUnderlineOffset: 3 }}
                >
                  Release Notes &amp; SHA256SUMS ↗
                </a>
              </div>

              <button
                type="button"
                onClick={() => setColophonOpen((prev) => !prev)}
                style={{
                  border: "1px solid var(--hairline)",
                  background: "var(--bg-elevated)",
                  color: "var(--ink-2)",
                  fontFamily: "var(--font-mono)",
                  fontSize: 10.5,
                  padding: "5px 10px",
                  borderRadius: 2,
                  cursor: "pointer",
                }}
              >
                {colophonOpen ? "▾ " : "▸ "}
                {t.acquire.colophonToggle}
              </button>
            </div>

            {colophonOpen && (
              <div
                style={{
                  marginTop: 16,
                  padding: "16px 20px",
                  border: "1px solid var(--hairline)",
                  background: "var(--bg-elevated)",
                  fontFamily: "var(--font-mono)",
                  fontSize: 11.5,
                  color: "var(--ink-2)",
                  display: "grid",
                  gridTemplateColumns: "repeat(auto-fit, minmax(260px, 1fr))",
                  gap: 16,
                }}
              >
                <div>
                  <div style={{ color: "var(--ink-3)", fontSize: 10, marginBottom: 4 }}>
                    CLOUD GATEWAY TELEMETRY
                  </div>
                  <div>
                    Supabase Backend:{" "}
                    <strong style={{ color: supabaseConfigured ? "#16a34a" : "#d97706" }}>
                      {supabaseConfigured ? "ONLINE (RLS ACTIVE)" : "GUEST / GITHUB FALLBACK MODE"}
                    </strong>
                  </div>
                  <div style={{ marginTop: 4 }}>Release Upstream: {repo}</div>
                </div>

                <div>
                  <div style={{ color: "var(--ink-3)", fontSize: 10, marginBottom: 4 }}>
                    ACTIVE REST / PKCE ENDPOINTS
                  </div>
                  <div style={{ lineHeight: 1.65 }}>
                    GET /api/health · GET /api/ota/check · GET,POST /api/hub · GET,PUT /api/sync ·
                    POST /api/bugs · GET /api/auth/login
                  </div>
                </div>
              </div>
            )}
          </footer>
        </section>
      </main>
    </div>
  );
}
