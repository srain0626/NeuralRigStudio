import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  metadataBase: new URL("https://neuralrigstudio.seltum.xyz"),
  title: "NeuralRig Studio — Live SPICE & .NAM Guitar Workstation",
  description:
    "Component-level SPICE circuit solver, 2x8 parallel .NAM routing, and AI schematic pedal creation at 0 B audio-thread latency.",
  openGraph: {
    title: "NeuralRig Studio",
    description:
      "Live circuit physics, 2x8 parallel .NAM routing, and one-command AI pedal creation for Windows.",
    type: "website",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ko" suppressHydrationWarning>
      <head>
        <link rel="preconnect" href="https://cdn.jsdelivr.net" crossOrigin="anonymous" />
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
        <link
          rel="preload"
          as="font"
          type="font/woff2"
          href="https://cdn.jsdelivr.net/gh/projectnoonnu/2408-3@1.0/Paperlogy-3Light.woff2"
          crossOrigin="anonymous"
        />
        <link
          rel="preload"
          as="font"
          type="font/woff2"
          href="https://cdn.jsdelivr.net/gh/sun-typeface/SUIT@2/fonts/variable/woff2/SUIT-Variable.woff2"
          crossOrigin="anonymous"
        />
        <link
          rel="stylesheet"
          href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500&family=Space+Grotesk:wght@300;400;500&display=swap"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
