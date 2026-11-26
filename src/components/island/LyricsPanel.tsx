import React, { useEffect, useMemo, useRef, useState } from 'react';
import {
  Check,
  Maximize2,
  Pause,
  Play,
  Search,
  SkipBack,
  SkipForward,
  X,
} from 'lucide-react';
import { LRCLIB_CATALOG } from '../../data/mockData';
import { formatTime, useNova } from '../../context/NovaContext';
import { SafeMediaImage } from '../ui/Controls';

export function LyricsPanel() {
  const {
    currentTrack,
    currentTime,
    isPlaying,
    togglePlay,
    nextTrack,
    prevTrack,
    seekTo,
    applyLrclibEntry,
    setFullscreenLyricsOpen,
    settings,
  } = useNova();

  const [searchOpen, setSearchOpen] = useState(false);
  const [query, setQuery] = useState('');
  const activeLineRef = useRef<HTMLButtonElement | null>(null);

  const activeIndex = useMemo(() => {
    let idx = 0;
    for (let i = 0; i < currentTrack.lyrics.length; i++) {
      if (currentTime >= currentTrack.lyrics[i].time) {
        idx = i;
      } else {
        break;
      }
    }
    return idx;
  }, [currentTrack.lyrics, currentTime]);

  useEffect(() => {
    if (!searchOpen && activeLineRef.current) {
      activeLineRef.current.scrollIntoView({
        behavior: settings.lyrics.smoothScroll ? 'smooth' : 'auto',
        block: 'center',
      });
    }
  }, [activeIndex, searchOpen, settings.lyrics.smoothScroll]);

  const searchResults = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return LRCLIB_CATALOG;
    return LRCLIB_CATALOG.filter(
      (entry) =>
        entry.trackName.toLowerCase().includes(q) ||
        entry.artistName.toLowerCase().includes(q) ||
        entry.albumName.toLowerCase().includes(q)
    );
  }, [query]);

  return (
    <div className="flex flex-col gap-3">
      {/* Header Row */}
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-2.5 min-w-0">
          <SafeMediaImage
            src={currentTrack.artwork}
            alt={currentTrack.title}
            className="w-8 h-8 rounded-lg object-cover shrink-0 border border-white/10"
          />
          <div className="min-w-0">
            <div className="text-[13px] font-semibold text-white truncate">
              {currentTrack.title}
            </div>
            <div className="flex items-center gap-1.5 text-[11px] text-zinc-400">
              <span className="truncate">{currentTrack.artist}</span>
              <span aria-hidden="true">·</span>
              <span style={{ color: 'var(--nova-accent)' }}>{currentTrack.lyricsSource}</span>
              {settings.lyrics.lrclibFallback && (
                <>
                  <span aria-hidden="true">·</span>
                  <span>{currentTrack.isSynced ? 'Synced LRC' : 'Plain'}</span>
                </>
              )}
            </div>
          </div>
        </div>

        <div className="flex items-center gap-1 shrink-0">
          <button
            type="button"
            onClick={() => setSearchOpen((o) => !o)}
            className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11.5px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
              searchOpen
                ? 'bg-white/[0.14] text-white'
                : 'text-zinc-300 bg-white/[0.05] hover:bg-white/[0.10] hover:text-white'
            }`}
          >
            {searchOpen ? <X className="w-3.5 h-3.5" /> : <Search className="w-3.5 h-3.5" />}
            <span>{searchOpen ? 'Close Search' : 'LRCLIB Search'}</span>
          </button>
          <button
            type="button"
            onClick={() => setFullscreenLyricsOpen(true)}
            className="p-1.5 rounded-lg text-zinc-400 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
            title="Fullscreen Lyrics"
            aria-label="Open Fullscreen Lyrics"
          >
            <Maximize2 className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Manual LRCLIB Search Interface OR Synchronized Lyrics Stream */}
      {searchOpen ? (
        <div className="flex flex-col gap-2.5">
          <div className="relative">
            <Search className="w-3.5 h-3.5 text-zinc-500 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search track, artist, or album on LRCLIB..."
              className="w-full bg-white/[0.05] border border-white/[0.09] rounded-lg pl-8 pr-3 py-2 text-[12.5px] text-white placeholder:text-zinc-500 focus:outline-none focus:border-white/25"
              autoFocus
            />
          </div>

          <div className="max-h-44 overflow-y-auto nova-scroll divide-y divide-white/[0.06]">
            {searchResults.length === 0 ? (
              <div className="py-6 text-center text-[12px] text-zinc-500">
                No matching LRCLIB records for "{query}".
              </div>
            ) : (
              searchResults.map((entry) => (
                <div
                  key={entry.id}
                  className="flex items-center justify-between gap-3 py-2.5 px-1.5 hover:bg-white/[0.04] rounded-md transition-colors"
                >
                  <div className="min-w-0">
                    <div className="text-[12.5px] font-medium text-zinc-100 truncate">
                      {entry.trackName}
                    </div>
                    <div className="text-[11px] text-zinc-400 truncate">
                      {entry.artistName} · {entry.source} · {formatTime(entry.duration)}
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => {
                      applyLrclibEntry(entry);
                      setSearchOpen(false);
                    }}
                    className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] font-medium bg-white/[0.09] hover:bg-white/[0.16] text-white whitespace-nowrap shrink-0 cursor-pointer"
                  >
                    <Check className="w-3 h-3" />
                    <span>Apply</span>
                  </button>
                </div>
              ))
            )}
          </div>
        </div>
      ) : (
        <div
          className="h-44 overflow-y-auto nova-scroll px-2 py-8 flex flex-col gap-2.5 relative"
          style={{
            maskImage:
              'linear-gradient(to bottom, transparent 0%, black 18%, black 82%, transparent 100%)',
            WebkitMaskImage:
              'linear-gradient(to bottom, transparent 0%, black 18%, black 82%, transparent 100%)',
          }}
        >
          {currentTrack.lyrics.map((line, idx) => {
            const isActive = idx === activeIndex;
            const distance = Math.abs(idx - activeIndex);
            return (
              <button
                key={`${line.time}-${idx}`}
                ref={isActive ? activeLineRef : null}
                type="button"
                onClick={() => seekTo(line.time)}
                className={`text-left transition-all duration-300 cursor-pointer rounded-md px-2 py-0.5 ${
                  isActive
                    ? 'text-white font-semibold'
                    : distance === 1
                    ? 'text-zinc-400 hover:text-zinc-200'
                    : 'text-zinc-600 hover:text-zinc-400'
                }`}
                style={{
                  fontSize: `${isActive ? settings.lyrics.fontSize : Math.max(12, settings.lyrics.fontSize - 2)}px`,
                  lineHeight: settings.lyrics.lineSpacing,
                  filter:
                    !isActive && settings.lyrics.blurInactiveLines && distance > 1
                      ? 'blur(0.8px)'
                      : 'none',
                }}
              >
                <span className="block">{line.text}</span>
                {isActive && settings.lyrics.showTranslations && line.translation && (
                  <span className="block text-[11.5px] font-normal text-zinc-400 mt-0.5">
                    {line.translation}
                  </span>
                )}
              </button>
            );
          })}
        </div>
      )}

      {/* Mini Playback Strip */}
      <div className="flex items-center justify-between pt-2 border-t border-white/[0.07]">
        <span className="font-mono-tabular text-[11px] text-zinc-400">
          {formatTime(currentTime)} / {formatTime(currentTrack.duration)}
        </span>
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={prevTrack}
            className="p-1.5 rounded-md text-zinc-400 hover:text-white hover:bg-white/[0.08] cursor-pointer"
            aria-label="Previous track"
          >
            <SkipBack className="w-3.5 h-3.5 fill-current" />
          </button>
          <button
            type="button"
            onClick={togglePlay}
            className="w-7 h-7 rounded-full bg-white text-zinc-950 flex items-center justify-center cursor-pointer"
            aria-label={isPlaying ? 'Pause' : 'Play'}
          >
            {isPlaying ? (
              <Pause className="w-3.5 h-3.5 fill-current" />
            ) : (
              <Play className="w-3.5 h-3.5 fill-current ml-0.5" />
            )}
          </button>
          <button
            type="button"
            onClick={nextTrack}
            className="p-1.5 rounded-md text-zinc-400 hover:text-white hover:bg-white/[0.08] cursor-pointer"
            aria-label="Next track"
          >
            <SkipForward className="w-3.5 h-3.5 fill-current" />
          </button>
        </div>
      </div>
    </div>
  );
}
