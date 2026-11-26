import React, { useState } from 'react';
import { useNova } from '../../context/NovaContext';
import { AudioEqualizer } from '../island/AudioEqualizer';
import { SafeMediaImage } from '../ui/Controls';

/**
 * Embedded live Dynamic Island preview inside Settings > Appearance.
 * Mirrors width, height, corner radius, surface theme, opacity, typography, and accent live.
 */
export function AppearanceLivePreview() {
  const { settings, currentTrack, isPlaying, appShortName } = useNova();
  const [previewMode, setPreviewMode] = useState<'music' | 'compact'>('music');

  const app = settings.appearance;
  const alpha = app.opacity / 100;
  const surfaceBg =
    app.theme === 'pure-black'
      ? `rgba(0, 0, 0, ${alpha})`
      : app.theme === 'obsidian'
      ? `rgba(10, 11, 14, ${alpha})`
      : app.theme === 'graphite'
      ? `rgba(22, 23, 27, ${alpha})`
      : `rgba(0, 0, 0, 1)`;

  return (
    <div className="mb-4 rounded-xl border border-zinc-200/90 dark:border-white/[0.08] bg-zinc-100/80 dark:bg-white/[0.025] p-4 flex flex-col gap-3">
      <div className="flex items-center justify-between gap-2">
        <span className="text-[12px] font-medium text-zinc-800 dark:text-zinc-200">
          Live Island Preview
        </span>
        <div className="flex items-center gap-1 p-0.5 rounded-lg bg-zinc-200/80 dark:bg-white/[0.06]">
          <button
            type="button"
            onClick={() => setPreviewMode('music')}
            className={`px-2 py-0.5 rounded-md text-[11px] font-medium transition-colors cursor-pointer ${
              previewMode === 'music'
                ? 'bg-white text-zinc-900 shadow-sm dark:bg-white/[0.14] dark:text-white'
                : 'text-zinc-600 dark:text-zinc-400'
            }`}
          >
            Music Pill
          </button>
          <button
            type="button"
            onClick={() => setPreviewMode('compact')}
            className={`px-2 py-0.5 rounded-md text-[11px] font-medium transition-colors cursor-pointer ${
              previewMode === 'compact'
                ? 'bg-white text-zinc-900 shadow-sm dark:bg-white/[0.14] dark:text-white'
                : 'text-zinc-600 dark:text-zinc-400'
            }`}
          >
            Compact
          </button>
        </div>
      </div>

      {/* Stage Backdrop */}
      <div className="h-24 rounded-xl border border-zinc-300/80 dark:border-white/[0.08] bg-gradient-to-r from-zinc-200 via-zinc-100 to-zinc-200 dark:from-[#0A0C12] dark:via-[#141722] dark:to-[#0A0C12] flex items-center justify-center px-4 overflow-hidden">
        <div
          className="flex items-center justify-between gap-3 px-3 text-white transition-all duration-200 shadow-xl"
          style={{
            width:
              previewMode === 'compact'
                ? `min(${app.islandWidth}px, 100%)`
                : `min(${Math.max(app.islandWidth + 100, 310)}px, 100%)`,
            height: `${previewMode === 'compact' ? app.islandHeight : Math.max(40, app.islandHeight + 4)}px`,
            borderRadius: `${app.cornerRadius}px`,
            backgroundColor: surfaceBg,
            border:
              app.theme === 'contrast'
                ? '1px solid rgba(255,255,255,0.28)'
                : '1px solid rgba(255,255,255,0.10)',
            fontSize: `${app.fontSize}px`,
            fontWeight: app.fontWeight,
          }}
        >
          {previewMode === 'compact' ? (
            <>
              <span className="flex items-center gap-2 min-w-0">
                <span
                  className="w-1.5 h-1.5 rounded-full shrink-0"
                  style={{ backgroundColor: app.accentColor }}
                />
                <span className="truncate">{appShortName}</span>
              </span>
              <span className="font-mono-tabular text-[11px] text-zinc-400 shrink-0">Ready</span>
            </>
          ) : (
            <>
              <span className="flex items-center gap-2 min-w-0">
                <SafeMediaImage
                  src={currentTrack.artwork}
                  alt={currentTrack.title}
                  className="w-6 h-6 rounded-full object-cover shrink-0 border border-white/15"
                />
                <span className="truncate">{currentTrack.title}</span>
              </span>
              <AudioEqualizer
                isPlaying={isPlaying}
                bpm={currentTrack.bpm}
                bars={5}
                height={12}
                reduceMotion={settings.animation.reduceMotion}
                accentColor={app.accentColor}
              />
            </>
          )}
        </div>
      </div>
    </div>
  );
}
