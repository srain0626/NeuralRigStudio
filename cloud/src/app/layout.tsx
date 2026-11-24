import type { Metadata } from "next";
import { GoogleAnalytics, GoogleTagManager } from "@next/third-parties/google";
import { DEFAULT_GTM_ID, HUB_SITE_URL, SITE_URL } from "@/lib/siteConfig";
import "./globals.css";

const GA_ID = process.env.NEXT_PUBLIC_GA_ID?.trim();
const GTM_ID = (process.env.NEXT_PUBLIC_GTM_ID ?? DEFAULT_GTM_ID).trim();
const GOOGLE_SITE_VERIFICATION = process.env.NEXT_PUBLIC_GOOGLE_SITE_VERIFICATION?.trim();

const SITE_TITLE = "NeuralRig Studio — Live SPICE & .NAM Guitar Workstation";
const SITE_DESCRIPTION =
  "Windows용 0 B 무할당 실시간 C#(.NET 10, AVX2/FMA) 기타 DSP 워크스테이션. 실시간 MNA SPICE 회로 시뮬레이션, 네이티브 .nam 신경망 추론, 2x8 병렬 시그널 체인 및 AI MCP 서버를 제공합니다.";

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: SITE_TITLE,
    template: "%s | NeuralRig Studio",
  },
  description: SITE_DESCRIPTION,
  applicationName: "NeuralRig Studio",
  category: "music",
  authors: [{ name: "Ray Kim", url: "https://github.com/srain0626" }],
  creator: "Ray Kim",
  publisher: "seltum.xyz",
  keywords: [
    "NeuralRig Studio",
    "기타 앰프 시뮬레이터",
    "일렉기타 멀티이펙터 프로그램",
    "NAM 앰프 모델러",
    "SPICE guitar amp simulator",
    "Neural Amp Modeler Windows",
    "Zero-allocation C# audio DSP",
    "ASIO guitar workstation",
    "Ray Kim",
    "seltum.xyz",
  ],
  alternates: {
    canonical: "/",
  },
  robots: {
    index: true,
    follow: true,
    googleBot: {
      index: true,
      follow: true,
      "max-image-preview": "large",
      "max-snippet": -1,
      "max-video-preview": -1,
    },
  },
  ...(GOOGLE_SITE_VERIFICATION
    ? {
        verification: {
          google: GOOGLE_SITE_VERIFICATION,
        },
      }
    : {}),
  openGraph: {
    title: "NeuralRig Studio",
    description:
      "Live circuit physics, 2x8 parallel .NAM routing, and one-command AI pedal creation for Windows.",
    url: SITE_URL,
    siteName: "NeuralRig Studio",
    locale: "ko_KR",
    alternateLocale: ["en_US"],
    type: "website",
  },
  twitter: {
    card: "summary_large_image",
    title: SITE_TITLE,
    description:
      "Component-level analog SPICE modeling, native .nam SIMD inference, and 2x8 parallel guitar signal routing at 0 B RT allocation.",
  },
};

const JSON_LD = {
  "@context": "https://schema.org",
  "@graph": [
    {
      "@type": "WebSite",
      name: "NeuralRig Studio",
      url: SITE_URL,
      isPartOf: {
        "@type": "WebSite",
        name: "seltum.xyz",
        url: HUB_SITE_URL,
      },
    },
    {
      "@type": "SoftwareApplication",
      name: "NeuralRig Studio",
      url: SITE_URL,
      description: SITE_DESCRIPTION,
      applicationCategory: "MultimediaApplication",
      operatingSystem: "Windows",
      softwareVersion: "3.0.0",
      inLanguage: ["ko", "en"],
      isAccessibleForFree: true,
      offers: {
        "@type": "Offer",
        price: "0",
        priceCurrency: "USD",
      },
      author: {
        "@type": "Person",
        name: "Ray Kim",
        url: "https://github.com/srain0626",
      },
    },
  ],
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ko" suppressHydrationWarning>
      {!GA_ID && GTM_ID ? <GoogleTagManager gtmId={GTM_ID} /> : null}
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
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{ __html: JSON.stringify(JSON_LD) }}
        />
      </head>
      <body>{children}</body>
      {GA_ID ? <GoogleAnalytics gaId={GA_ID} /> : null}
    </html>
  );
}
