import React, { useEffect, useState } from 'react';
import { motion } from 'motion/react';

interface AudioEqualizerProps {
  isPlaying: boolean;
  bpm?: number;
  bars?: number;
  height?: number;
  reduceMotion?: boolean;
  accentColor?: string;
}

/**
 * Audio-reactive animated frequency equalizer.
 * Uses GPU-composited scaleY transforms paced to track BPM for smooth,
 * low-overhead OS-native motion without layout reflows.
 */
export function AudioEqualizer({
  isPlaying,
  bpm = 116,
  bars = 5,
  height = 14,
  reduceMotion = false,
  accentColor = 'var(--nova-accent)',
}: AudioEqualizerProps) {
  const [levels, setLevels] = useState<number[]>(() =>
    Array.from({ length: bars }, (_, i) => 0.25 + ((i * 19) % 50) / 100)
  );

  useEffect(() => {
    if (!isPlaying || reduceMotion) {
      setLevels(Array.from({ length: bars }, (_, i) => 0.18 + (i % 2) * 0.06));
      return;
    }

    const beatIntervalMs = Math.max(130, Math.min(260, Math.round((60000 / bpm) / 2.6)));
    let phase = 0;

    const id = window.setInterval(() => {
      phase += 1;
      setLevels(
        Array.from({ length: bars }, (_, i) => {
          // Shaped mid-band spectral envelope with organic harmonic variation
          const centerWeight = 1 - Math.abs(i - (bars - 1) / 2) / Math.max(1, bars * 0.65);
          const wave = Math.sin(phase * 0.9 + i * 1.35) * 0.34 + 0.52;
          const jitter = ((Math.sin(phase * 2.1 + i * 3.7) + 1) / 2) * 0.28;
          return Math.min(1, Math.max(0.18, (wave + jitter) * (0.62 + centerWeight * 0.45)));
        })
      );
    }, beatIntervalMs);

    return () => window.clearInterval(id);
  }, [isPlaying, bpm, bars, reduceMotion]);

  return (
    <div
      className="flex items-end gap-[2.5px] shrink-0"
      style={{ height: `${height}px` }}
      aria-hidden="true"
    >
      {levels.map((lvl, idx) => (
        <motion.span
          key={idx}
          animate={{ scaleY: lvl }}
          transition={
            reduceMotion
              ? { duration: 0.05 }
              : { type: 'spring', stiffness: 360, damping: 22, mass: 0.45 }
          }
          className="w-[2.5px] h-full rounded-full origin-bottom will-change-transform"
          style={{
            backgroundColor: accentColor,
            opacity: isPlaying ? 0.95 : 0.4,
          }}
        />
      ))}
    </div>
  );
}
