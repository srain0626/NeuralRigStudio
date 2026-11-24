import type { Metadata } from "next";

export const metadata: Metadata = {
  title: "NeuralRig Cloud",
  description: "Public Cloud Sync, Community Gear Hub, OTA Releases & Bug Telemetry for NeuralRig Studio",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <body
        style={{
          margin: 0,
          background: "#0c0f14",
          color: "#e6edf3",
          fontFamily: "ui-sans-serif, system-ui, sans-serif",
        }}
      >
        {children}
      </body>
    </html>
  );
}
