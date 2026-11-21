import React from "react"
import type { DeviceCategoryId } from "@/types/rig"

interface IconProps {
  className?: string
  strokeWidth?: number
}

export const NeuralPulseLogo: React.FC<IconProps> = ({ className = "w-5 h-5" }) => (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round" className={className}>
    <path d="M2 12h4l2.5-7 3.5 14 2.5-7H22" />
  </svg>
)

export const CategoryVectorIcon: React.FC<{ category: DeviceCategoryId; className?: string }> = ({
  category,
  className = "w-6 h-6",
}) => {
  switch (category) {
    case "amp":
      // Amp head chassis icon (matches screenshot red block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <rect x="4" y="7" width="20" height="14" rx="2" />
          <line x1="4" y1="13" x2="24" y2="13" />
          <circle cx="8" cy="10" r="0.9" fill="currentColor" />
          <circle cx="11.5" cy="10" r="0.9" fill="currentColor" />
          <circle cx="15" cy="10" r="0.9" fill="currentColor" />
          <line x1="19" y1="10" x2="21.5" y2="10" />
          <line x1="8" y1="17" x2="20" y2="17" opacity={0.55} />
        </svg>
      )
    case "nam":
      // Neural Capture horizontal-striped sphere (matches screenshot silver block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" className={className}>
          <circle cx="14" cy="14" r="8.5" opacity={0.35} />
          <line x1="8.5" y1="9" x2="19.5" y2="9" />
          <line x1="6.5" y1="11.5" x2="21.5" y2="11.5" />
          <line x1="6" y1="14" x2="22" y2="14" />
          <line x1="6.5" y1="16.5" x2="21.5" y2="16.5" />
          <line x1="8.5" y1="19" x2="19.5" y2="19" />
        </svg>
      )
    case "cab":
      // Speaker cone + 4 mounting dots (matches screenshot purple block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <circle cx="14" cy="14" r="6.8" />
          <circle cx="14" cy="14" r="2.2" fill="currentColor" />
          <circle cx="7" cy="7" r="0.9" fill="currentColor" />
          <circle cx="21" cy="7" r="0.9" fill="currentColor" />
          <circle cx="7" cy="21" r="0.9" fill="currentColor" />
          <circle cx="21" cy="21" r="0.9" fill="currentColor" />
        </svg>
      )
    case "drive":
      // Clipped overdrive sine wave (matches screenshot orange block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.6} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <path d="M5 17 C7 9, 8.5 8, 11.5 8 C13 8, 13.5 10.5, 14 14 C14.5 17.5, 15 20, 16.5 20 C19.5 20, 21 19, 23 11" />
        </svg>
      )
    case "delay":
      // Decaying echo waves (matches screenshot teal block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <path d="M6 7 L10.5 21 L14 8 L17.5 20 L20.5 11 L22.5 17" />
        </svg>
      )
    case "reverb":
      // 3D wireframe spatial cube (matches screenshot mint block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.4} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <polygon points="14,5.5 22,9.8 22,18.2 14,22.5 6,18.2 6,9.8" />
          <line x1="14" y1="5.5" x2="14" y2="22.5" opacity={0.5} />
          <line x1="6" y1="9.8" x2="14" y2="14" />
          <line x1="22" y1="9.8" x2="14" y2="14" />
          <line x1="6" y1="18.2" x2="14" y2="14" opacity={0.45} />
          <line x1="22" y1="18.2" x2="14" y2="14" opacity={0.45} />
        </svg>
      )
    case "mod":
      // Modulation wave with top/bottom phase triangles (matches screenshot green block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <path d="M6 15 C8.5 9.5, 11.5 9.5, 14 14 C16.5 18.5, 19.5 18.5, 22 13" />
          <polygon points="14,6 15.8,8.5 12.2,8.5" fill="currentColor" stroke="none" />
          <polygon points="14,22 15.8,19.5 12.2,19.5" fill="currentColor" stroke="none" />
        </svg>
      )
    case "eq":
      // 3-slider graphic EQ (matches screenshot blue block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" className={className}>
          <line x1="8.5" y1="6.5" x2="8.5" y2="21.5" />
          <circle cx="8.5" cy="11.5" r="1.6" fill="currentColor" />
          <line x1="14" y1="6.5" x2="14" y2="21.5" />
          <circle cx="14" cy="16.5" r="1.6" fill="currentColor" />
          <line x1="19.5" y1="6.5" x2="19.5" y2="21.5" />
          <circle cx="19.5" cy="10.5" r="1.6" fill="currentColor" />
        </svg>
      )
    case "dyn":
    default:
      // Compressor / Gate knee transfer curve (matches screenshot gray block)
      return (
        <svg viewBox="0 0 28 28" fill="none" stroke="currentColor" strokeWidth={1.5} strokeLinecap="round" strokeLinejoin="round" className={className}>
          <polyline points="6.5,6.5 6.5,21.5 21.5,21.5" opacity={0.5} />
          <path d="M6.5 21.5 L14 13 L21.5 10.5" />
          <path d="M6.5 21.5 L14 15.5 L21.5 15.5" opacity={0.45} />
        </svg>
      )
  }
}
