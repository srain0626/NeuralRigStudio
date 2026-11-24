import { ImageResponse } from "next/og";

export const alt =
  "NeuralRig Studio — Component-Level SPICE & Neural Guitar Workstation";
export const size = {
  width: 1200,
  height: 630,
};
export const contentType = "image/png";

export default function OpenGraphImage() {
  return new ImageResponse(
    (
      <div
        style={{
          width: "100%",
          height: "100%",
          display: "flex",
          flexDirection: "column",
          justifyContent: "space-between",
          backgroundColor: "#0a0a0d",
          color: "#f4f4f5",
          padding: "56px 64px",
          fontFamily: "sans-serif",
        }}
      >
        <div
          style={{
            display: "flex",
            justifyContent: "space-between",
            alignItems: "center",
            width: "100%",
          }}
        >
          <div
            style={{
              display: "flex",
              alignItems: "center",
              gap: "12px",
              border: "1px solid rgba(255,94,26,0.45)",
              backgroundColor: "rgba(255,94,26,0.1)",
              borderRadius: "999px",
              padding: "8px 18px",
              fontSize: "19px",
              color: "#FFB020",
              letterSpacing: "0.12em",
            }}
          >
            <span>NR-300 ACOUSTIC MONOGRAPH</span>
          </div>
          <div style={{ display: "flex", fontSize: "20px", color: "#a1a1aa" }}>
            neuralrigstudio.seltum.xyz
          </div>
        </div>

        <div style={{ display: "flex", flexDirection: "column", gap: "18px" }}>
          <div
            style={{
              display: "flex",
              fontSize: "66px",
              fontWeight: 700,
              letterSpacing: "-0.03em",
              lineHeight: 1.05,
            }}
          >
            NeuralRig Studio
          </div>
          <div
            style={{
              display: "flex",
              fontSize: "28px",
              color: "#d4d4d8",
              maxWidth: "1000px",
              lineHeight: 1.38,
            }}
          >
            Zero-allocation C# (.NET 10, AVX2/FMA) MNA SPICE circuit solver, native .nam inference, and 2x8 guitar signal routing.
          </div>
        </div>

        <div style={{ display: "flex", gap: "16px", width: "100%" }}>
          {[
            ["RT ALLOCATION", "0 B Heap"],
            ["RIGBENCH MATCH", "98.2% Verified"],
            ["6-BLOCK LATENCY", "0.0535 ms"],
            ["ARCHITECTURE", "2x8 Slots + MCP"],
          ].map(([label, val]) => (
            <div
              key={label}
              style={{
                display: "flex",
                flexDirection: "column",
                flex: 1,
                gap: "6px",
                border: "1px solid rgba(255,255,255,0.12)",
                backgroundColor: "rgba(255,255,255,0.03)",
                borderRadius: "16px",
                padding: "18px 20px",
              }}
            >
              <div style={{ display: "flex", fontSize: "14px", color: "#a1a1aa", letterSpacing: "0.1em" }}>
                {label}
              </div>
              <div style={{ display: "flex", fontSize: "22px", fontWeight: 700, color: "#FFB020" }}>
                {val}
              </div>
            </div>
          ))}
        </div>
      </div>
    ),
    { ...size }
  );
}
