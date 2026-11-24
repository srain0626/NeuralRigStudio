import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  metadataBase: new URL("https://neuralrigstudio.seltum.xyz"),
  title: "NeuralRig Studio — Component-Level SPICE & Neural Guitar Workstation",
  description:
    "Real-time MNA SPICE circuit solver, native AVX2/FMA .nam inference, and 2x8 parallel signal routing with 0 B audio-thread allocation for Windows.",
  openGraph: {
    title: "NeuralRig Studio — NR-300 Acoustic Monograph",
    description:
      "Component-level analog circuit modeling, native .nam inference, and 2x8 parallel guitar signal routing at 0 B RT allocation.",
    type: "website",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ko" data-chassis="putty" suppressHydrationWarning>
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
        <link
          rel="stylesheet"
          href="https://fonts.googleapis.com/css2?family=IBM+Plex+Mono:wght@400;500;600&family=Instrument+Serif:ital@0;1&family=Space+Grotesk:wght@500;600;700&display=swap"
        />
        <link
          rel="stylesheet"
          href="https://cdn.jsdelivr.net/gh/orioncactus/pretendard@v1.3.9/dist/web/variable/pretendardvariable-dynamic-subset.min.css"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
