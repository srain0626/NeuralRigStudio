"use client";

import React, { useEffect, useRef, useState } from "react";
import { FeatureTrioStages } from "./FeatureTrioStages";
import { HeroFloatingCards } from "./HeroFloatingCards";
import { Locale, MINIMAL_COPY } from "./LandingData";
import { StickyMorphShowcase } from "./StickyMorphShowcase";

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

export function LandingExperience({
  supabaseConfigured,
  repo,
  release,
  liveHubItems,
}: LandingExperienceProps) {
  const [locale, setLocale] = useState<Locale>("ko");
  const [introDone, setIntroDone] = useState(false);
  const [scrolled, setScrolled] = useState(false);
  const [trioVisible, setTrioVisible] = useState(false);
  const [outroVisible, setOutroVisible] = useState(false);
  const [typedCount, setTypedCount] = useState(0);

  const trioRef = useRef<HTMLDivElement | null>(null);
  const stickyTrackRef = useRef<HTMLElement | null>(null);
  const outroRef = useRef<HTMLElement | null>(null);

  const t = MINIMAL_COPY[locale];

  // Act 0: 0.85s Signature Center Mark -> 45° Chamfered Horizon Opening
  useEffect(() => {
    const id = window.setTimeout(() => setIntroDone(true), 820);
    return () => window.clearTimeout(id);
  }, []);

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 28);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  // Scroll-triggered reveals for Trio & Outro Typewriter
  useEffect(() => {
    const obs = new IntersectionObserver(
      (entries) => {
        entries.forEach((entry) => {
          if (entry.target === trioRef.current && entry.isIntersecting) {
            setTrioVisible(true);
          }
          if (entry.target === outroRef.current && entry.isIntersecting) {
            setOutroVisible(true);
          }
        });
      },
      { threshold: 0.16 }
    );

    if (trioRef.current) obs.observe(trioRef.current);
    if (outroRef.current) obs.observe(outroRef.current);
    return () => obs.disconnect();
  }, []);

  // Kinetic Typewriter Effect on Act 4 Closing Headline (00:16 Reference Homage)
  useEffect(() => {
    if (!outroVisible) return;
    setTypedCount(0);
    const fullLen = t.outroTyped.length;
    const id = window.setInterval(() => {
      setTypedCount((prev) => {
        if (prev >= fullLen) {
          window.clearInterval(id);
          return fullLen;
        }
        return prev + 1;
      });
    }, 34);
    return () => window.clearInterval(id);
  }, [outroVisible, t.outroTyped]);

  const installerHref =
    release.installerUrl ||
    `https://github.com/${repo}/releases/download/v${release.version}/NeuralRigStudio-v${release.version}-Setup-win-x64.exe`;
  const standaloneHref =
    release.standaloneExeUrl ||
    `https://github.com/${repo}/releases/download/v${release.version}/NeuralRigStudio-v${release.version}-win-x64-standalone.exe`;

  const jumpToStickyChapter = (idx: number) => {
    const el = stickyTrackRef.current;
    if (!el) return;
    const scrollable = el.offsetHeight - window.innerHeight;
    const fractions = [0.08, 0.5, 0.9];
    const targetTop =
      scrollable > 10
        ? el.offsetTop + scrollable * (fractions[idx] ?? 0.5)
        : el.offsetTop;
    window.scrollTo({ top: targetTop, behavior: "smooth" });
  };

  return (
    <>
      {/* ====================================================================
          ACT 0: 0.85s CENTER PULSE SYMBOL INTRO (00:18-00:20 HOMAGE)
         ==================================================================== */}
      <div className="nr-intro-veil" data-done={introDone} aria-hidden="true">
        <div className="nr-intro-mark">
          <svg width={26} height={26} viewBox="0 0 24 24" fill="none">
            <path
              d="M2 12h4l2.8-8 3.6 16 2.8-8H22"
              stroke="#0b0e10"
              strokeWidth={2.2}
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          <span>NeuralRig</span>
        </div>
      </div>

      {/* ====================================================================
          MINIMALIST TOP BAR
         ==================================================================== */}
      <header className="nr-header" data-scrolled={scrolled}>
        <a href="#top" className="nr-logo">
          <svg width={22} height={22} viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              d="M2 12h4l2.8-7.5 3.6 15 2.8-7.5H22"
              stroke="#0b0e10"
              strokeWidth={2.1}
              strokeLinecap="round"
              strokeLinejoin="round"
            />
          </svg>
          <span>NeuralRig</span>
        </a>

        <div className="nr-header-actions">
          <button
            type="button"
            className="nr-dot-btn"
            onClick={() => setLocale((prev) => (prev === "ko" ? "en" : "ko"))}
          >
            <span className="nr-dot" />
            <span>{locale === "ko" ? "EN" : "KR"}</span>
          </button>

          <a href="#download" className="nr-solid-btn" style={{ padding: "10px 16px" }}>
            <span className="nr-arrow">→</span>
            <span>{t.downloadBtn}</span>
          </a>
        </div>
      </header>

      <main id="top">
        {/* ==================================================================
            ACT 1: AIRY HERO + DUAL LAYERED PARALLAX CARDS + 45° CAD HORIZON
           ================================================================== */}
        <section className="nr-hero" data-ready={introDone}>
          <div className="nr-hero-aura" aria-hidden="true" />

          <div className="nr-hero-inner">
            <div className="nr-hero-enter">
              <div className="nr-whisper">{t.heroWhisper}</div>
              <h1 className="nr-display-h1">
                {t.heroLines[0]}
                <br />
                {t.heroLines[1]}
                <br />
                {t.heroLines[2]}
              </h1>

              <div style={{ marginTop: 36 }}>
                <a href={installerHref} className="nr-solid-btn">
                  <span className="nr-arrow">→</span>
                  <span>{t.downloadBtn}</span>
                </a>
              </div>
            </div>

            <HeroFloatingCards locale={locale} ready={introDone} />
          </div>

          {/* 45° Chamfered CAD Horizon Line Drawing Outward (00:00 / 00:20 Reference) */}
          <svg
            className="nr-cad-horizon"
            viewBox="0 0 1340 42"
            fill="none"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <path
              className="nr-cad-path"
              d="M 0 38 L 210 38 L 242 8 L 820 8 L 852 38 L 1280 38 L 1308 12 L 1340 12"
              stroke="rgba(11, 14, 16, 0.24)"
              strokeWidth={1.15}
            />
          </svg>
        </section>

        {/* ==================================================================
            ACT 2: 3 CORE WEAPONS — DARK OBSIDIAN ANIMATED STAGES
           ================================================================== */}
        <div ref={trioRef} className="nr-reveal" data-visible={trioVisible}>
          <FeatureTrioStages
            locale={locale}
            installerHref={installerHref}
            onJumpToChapter={jumpToStickyChapter}
          />
        </div>

        {/* ==================================================================
            ACT 3: 330VH STICKY SCROLL-MORPHING CENTERPIECE
           ================================================================== */}
        <StickyMorphShowcase
          locale={locale}
          trackRef={stickyTrackRef}
          liveHubItem={liveHubItems[0]}
        />

        {/* ==================================================================
            ACT 4: 45° INTERLOCKING CHAMFERED CLOSING STAGE & DOWNLOAD
           ================================================================== */}
        <footer id="download" ref={outroRef} className="nr-outro">
          <div className="nr-outro-chamfer" aria-hidden="true" />

          <div className="nr-outro-inner">
            <div
              style={{
                display: "grid",
                gridTemplateColumns: "repeat(auto-fit, minmax(280px, 1fr))",
                alignItems: "center",
                gap: 40,
                paddingBottom: 92,
              }}
            >
              {/* Left CAD Line Accent */}
              <div
                style={{
                  height: 1,
                  background: "rgba(255, 255, 255, 0.28)",
                  maxWidth: 420,
                }}
              />

              {/* Right Kinetic Typewriter Headline + Clean Download Pair */}
              <div>
                <h2
                  style={{
                    margin: 0,
                    fontSize: "clamp(1.9rem, 3.2vw, 2.85rem)",
                    fontWeight: 400,
                    lineHeight: 1.16,
                    letterSpacing: "-0.03em",
                    minHeight: "2.4em",
                  }}
                >
                  {t.outroTyped.slice(0, typedCount)}
                  <span
                    style={{
                      display: "inline-block",
                      width: 2,
                      height: "0.9em",
                      background: "#ff7730",
                      marginLeft: 4,
                      verticalAlign: "-0.08em",
                      animation: "nrBlinkCursor 0.9s infinite",
                    }}
                  />
                </h2>

                <div
                  style={{
                    marginTop: 28,
                    display: "flex",
                    flexWrap: "wrap",
                    gap: 12,
                  }}
                >
                  <a
                    href={installerHref}
                    className="nr-solid-btn"
                    style={{
                      background: "#f5f7f6",
                      color: "#0b0e10",
                      borderColor: "#f5f7f6",
                    }}
                  >
                    <span className="nr-arrow">↓</span>
                    <span>{t.installerBtn}</span>
                  </a>

                  <a href={standaloneHref} className="nr-ghost-btn">
                    <span style={{ color: "#ff8c42", fontFamily: "var(--font-mono)" }}>↗</span>
                    <span>{t.portableBtn}</span>
                  </a>
                </div>
              </div>
            </div>

            {/* Minimal Bottom Brand & Hairline */}
            <div
              style={{
                display: "flex",
                alignItems: "center",
                justifyContent: "space-between",
                paddingBottom: 20,
              }}
            >
              <div style={{ display: "inline-flex", alignItems: "center", gap: 10, fontSize: 22 }}>
                <svg width={24} height={24} viewBox="0 0 24 24" fill="none">
                  <path
                    d="M2 12h4l2.8-7.5 3.6 15 2.8-7.5H22"
                    stroke="#f5f7f6"
                    strokeWidth={2}
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                </svg>
                <span style={{ fontWeight: 450, letterSpacing: "-0.03em" }}>NeuralRig</span>
              </div>

              <span
                style={{
                  fontFamily: "var(--font-mono)",
                  fontSize: 11,
                  color: "rgba(255,255,255,0.5)",
                }}
              >
                v{release.version} · Windows x64
              </span>
            </div>

            <div style={{ height: 1, background: "rgba(255, 255, 255, 0.16)" }} />

            <div
              style={{
                paddingTop: 18,
                display: "flex",
                flexWrap: "wrap",
                justifyContent: "space-between",
                gap: 14,
                fontFamily: "var(--font-mono)",
                fontSize: 11,
                color: "rgba(255, 255, 255, 0.48)",
              }}
            >
              <span>© 2026 NEURALRIG STUDIO · MIT LICENSE</span>

              <div style={{ display: "flex", alignItems: "center", gap: 20 }}>
                <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
                  <span
                    style={{
                      width: 6,
                      height: 6,
                      borderRadius: "50%",
                      background: supabaseConfigured ? "#4ade80" : "#f59e0b",
                    }}
                  />
                  <span>CLOUD {supabaseConfigured ? "ONLINE" : "READY"}</span>
                </span>
                <a
                  href={`https://github.com/${repo}`}
                  target="_blank"
                  rel="noreferrer"
                  style={{ color: "rgba(255,255,255,0.72)" }}
                >
                  GITHUB
                </a>
                <a
                  href={release.releasePageUrl}
                  target="_blank"
                  rel="noreferrer"
                  style={{ color: "rgba(255,255,255,0.72)" }}
                >
                  RELEASES
                </a>
              </div>
            </div>
          </div>
        </footer>
      </main>
    </>
  );
}
