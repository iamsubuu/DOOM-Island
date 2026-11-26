import React, { useEffect, useMemo, useRef, useState } from 'react';
import { AnimatePresence, motion } from 'motion/react';
import {
  Check,
  Disc3,
  Pause,
  Play,
  Repeat,
  Search,
  Shuffle,
  SkipBack,
  SkipForward,
  X,
} from 'lucide-react';
import { LRCLIB_CATALOG } from '../../data/mockData';
import { formatTime, useNova } from '../../context/NovaContext';
import { SafeMediaImage } from '../ui/Controls';

export function FullscreenLyrics() {
  const {
    appShortName,
    fullscreenLyricsOpen,
    setFullscreenLyricsOpen,
    currentTrack,
    currentTime,
    isPlaying,
    shuffle,
    repeat,
    togglePlay,
    nextTrack,
    prevTrack,
    seekTo,
    setShuffle,
    setRepeat,
    applyLrclibEntry,
    settings,
  } = useNova();

  const [searchDrawerOpen, setSearchDrawerOpen] = useState(false);
  const [query, setQuery] = useState('');
  const activeRef = useRef<HTMLButtonElement | null>(null);

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
    if (fullscreenLyricsOpen && activeRef.current) {
      activeRef.current.scrollIntoView({
        behavior: settings.lyrics.smoothScroll ? 'smooth' : 'auto',
        block: 'center',
      });
    }
  }, [activeIndex, fullscreenLyricsOpen, settings.lyrics.smoothScroll]);

  useEffect(() => {
    if (!fullscreenLyricsOpen) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setFullscreenLyricsOpen(false);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [fullscreenLyricsOpen, setFullscreenLyricsOpen]);

  const searchResults = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return LRCLIB_CATALOG;
    return LRCLIB_CATALOG.filter(
      (item) =>
        item.trackName.toLowerCase().includes(q) ||
        item.artistName.toLowerCase().includes(q) ||
        item.albumName.toLowerCase().includes(q)
    );
  }, [query]);

  const progressPct =
    currentTrack.duration > 0 ? Math.min(100, (currentTime / currentTrack.duration) * 100) : 0;

  return (
    <AnimatePresence>
      {fullscreenLyricsOpen && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.2 }}
          className="fixed inset-0 z-[80] bg-[#06070A] text-white flex flex-col overflow-hidden"
        >
          {/* Ambient Blurred Artwork Backdrop */}
          <div className="absolute inset-0 overflow-hidden pointer-events-none">
            <SafeMediaImage
              src={currentTrack.artwork}
              alt=""
              className="w-full h-full object-cover scale-125 blur-[90px]"
            />
            <div
              className="absolute inset-0 bg-[#06070A]"
              style={{ opacity: 1 - settings.lyrics.fullscreenBackdropOpacity / 120 }}
            />
            <div className="absolute inset-0 bg-gradient-to-b from-black/60 via-black/30 to-black/80" />
          </div>

          {/* Top Bar */}
          <header className="relative z-10 flex items-center justify-between gap-6 px-8 py-5 border-b border-white/[0.08]">
            <div className="flex items-center gap-3 text-[12.5px] text-zinc-400">
              <Disc3 className="w-4 h-4 text-zinc-200" />
              <span className="font-medium text-zinc-100">{appShortName} Lyrics</span>
              <span aria-hidden="true">·</span>
              <span style={{ color: 'var(--nova-accent)' }}>{currentTrack.lyricsSource}</span>
              <span aria-hidden="true">·</span>
              <span>{currentTrack.isSynced ? 'Time-Synced LRC' : 'Static'}</span>
              {settings.lyrics.lrclibFallback && (
                <>
                  <span aria-hidden="true">·</span>
                  <span>LRCLIB Ready</span>
                </>
              )}
            </div>

            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => setSearchDrawerOpen((v) => !v)}
                className={`flex items-center gap-2 px-3.5 py-1.5 rounded-lg text-[12.5px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
                  searchDrawerOpen
                    ? 'bg-white text-zinc-950'
                    : 'bg-white/[0.08] text-zinc-200 hover:bg-white/[0.15]'
                }`}
              >
                <Search className="w-3.5 h-3.5" />
                <span>Manual LRCLIB Search</span>
              </button>
              <button
                type="button"
                onClick={() => setFullscreenLyricsOpen(false)}
                className="p-2 rounded-lg bg-white/[0.08] hover:bg-white/[0.16] text-zinc-200 transition-colors cursor-pointer"
                aria-label="Exit Fullscreen Lyrics"
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          </header>

          {/* Main Split Stage */}
          <div className="relative z-10 flex-1 min-h-0 flex max-w-6xl w-full mx-auto px-8 py-8 gap-12 items-center">
            {/* Left Track Column */}
            <div className="hidden md:flex flex-col w-80 shrink-0 gap-6">
              <div className="aspect-square w-full rounded-2xl overflow-hidden border border-white/10 shadow-2xl">
                <SafeMediaImage
                  src={currentTrack.artwork}
                  alt={currentTrack.title}
                  className="w-full h-full object-cover"
                />
              </div>

              <div>
                <h2 className="text-2xl font-semibold tracking-tight text-white leading-tight">
                  {currentTrack.title}
                </h2>
                <p className="text-[14px] text-zinc-300 mt-1">{currentTrack.artist}</p>
                <p className="text-[12.5px] text-zinc-500 mt-0.5">
                  {currentTrack.album} · {currentTrack.bpm} BPM
                </p>
              </div>

              {/* Progress + Transport */}
              <div className="flex flex-col gap-3">
                <div className="relative flex items-center">
                  <div className="w-full h-1.5 rounded-full bg-white/[0.14] overflow-hidden">
                    <div
                      className="h-full rounded-full"
                      style={{
                        width: `${progressPct}%`,
                        backgroundColor: 'var(--nova-accent)',
                      }}
                    />
                  </div>
                  <input
                    type="range"
                    min={0}
                    max={currentTrack.duration}
                    value={currentTime}
                    onChange={(e) => seekTo(Number(e.target.value))}
                    aria-label="Seek position"
                    className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
                  />
                </div>
                <div className="flex justify-between font-mono-tabular text-[11.5px] text-zinc-400">
                  <span>{formatTime(currentTime)}</span>
                  <span>{formatTime(currentTrack.duration)}</span>
                </div>

                <div className="flex items-center justify-between pt-2">
                  <button
                    type="button"
                    onClick={() => setShuffle((s) => !s)}
                    className={`p-2 rounded-lg transition-colors cursor-pointer ${
                      shuffle ? 'text-white bg-white/[0.12]' : 'text-zinc-400 hover:text-white'
                    }`}
                    aria-label="Shuffle"
                  >
                    <Shuffle className="w-4 h-4" />
                  </button>
                  <button
                    type="button"
                    onClick={prevTrack}
                    className="p-2 rounded-lg text-zinc-200 hover:text-white hover:bg-white/[0.08] cursor-pointer"
                    aria-label="Previous track"
                  >
                    <SkipBack className="w-5 h-5 fill-current" />
                  </button>
                  <button
                    type="button"
                    onClick={togglePlay}
                    className="w-12 h-12 rounded-full bg-white text-zinc-950 flex items-center justify-center hover:scale-[1.04] transition-transform cursor-pointer shadow-xl"
                    aria-label={isPlaying ? 'Pause' : 'Play'}
                  >
                    {isPlaying ? (
                      <Pause className="w-5 h-5 fill-current" />
                    ) : (
                      <Play className="w-5 h-5 fill-current ml-0.5" />
                    )}
                  </button>
                  <button
                    type="button"
                    onClick={nextTrack}
                    className="p-2 rounded-lg text-zinc-200 hover:text-white hover:bg-white/[0.08] cursor-pointer"
                    aria-label="Next track"
                  >
                    <SkipForward className="w-5 h-5 fill-current" />
                  </button>
                  <button
                    type="button"
                    onClick={() => setRepeat((r) => !r)}
                    className={`p-2 rounded-lg transition-colors cursor-pointer ${
                      repeat ? 'text-white bg-white/[0.12]' : 'text-zinc-400 hover:text-white'
                    }`}
                    aria-label="Repeat"
                  >
                    <Repeat className="w-4 h-4" />
                  </button>
                </div>
              </div>
            </div>

            {/* Right Synchronized Lyrics Stream */}
            <div
              className="flex-1 h-full overflow-y-auto nova-scroll py-28 pr-4 flex flex-col gap-5"
              style={{
                maskImage:
                  'linear-gradient(to bottom, transparent 0%, black 16%, black 84%, transparent 100%)',
                WebkitMaskImage:
                  'linear-gradient(to bottom, transparent 0%, black 16%, black 84%, transparent 100%)',
              }}
            >
              {currentTrack.lyrics.map((line, idx) => {
                const isActive = idx === activeIndex;
                const dist = Math.abs(idx - activeIndex);
                return (
                  <button
                    key={`${line.time}-${idx}`}
                    ref={isActive ? activeRef : null}
                    type="button"
                    onClick={() => seekTo(line.time)}
                    className={`text-left transition-all duration-300 cursor-pointer rounded-xl px-3 py-1 ${
                      isActive
                        ? 'text-white font-semibold'
                        : dist === 1
                        ? 'text-zinc-400 hover:text-zinc-200'
                        : 'text-zinc-600 hover:text-zinc-400'
                    }`}
                    style={{
                      fontSize: `${isActive ? Math.max(26, settings.lyrics.fontSize + 12) : Math.max(20, settings.lyrics.fontSize + 5)}px`,
                      lineHeight: 1.38,
                      filter:
                        !isActive && settings.lyrics.blurInactiveLines && dist > 1
                          ? 'blur(1.2px)'
                          : 'none',
                    }}
                  >
                    <span className="block">{line.text}</span>
                    {isActive && settings.lyrics.showTranslations && line.translation && (
                      <span className="block text-[15px] font-normal text-zinc-400 mt-1.5">
                        {line.translation}
                      </span>
                    )}
                  </button>
                );
              })}
            </div>
          </div>

          {/* Slide-over Manual LRCLIB Search Drawer */}
          <AnimatePresence>
            {searchDrawerOpen && (
              <motion.aside
                initial={{ x: '100%' }}
                animate={{ x: 0 }}
                exit={{ x: '100%' }}
                transition={{ type: 'spring', stiffness: 360, damping: 34 }}
                className="fixed top-[69px] right-0 bottom-0 w-full max-w-md z-20 bg-[#0D0F14]/95 backdrop-blur-2xl border-l border-white/[0.09] p-6 flex flex-col gap-4"
              >
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="text-[15px] font-semibold text-white">LRCLIB Manual Lookup</h3>
                    <p className="text-[12px] text-zinc-400">
                      Override synchronized timestamps for the active track
                    </p>
                  </div>
                  <button
                    type="button"
                    onClick={() => setSearchDrawerOpen(false)}
                    className="p-1.5 rounded-lg text-zinc-400 hover:text-white hover:bg-white/[0.08] cursor-pointer"
                    aria-label="Close search drawer"
                  >
                    <X className="w-4 h-4" />
                  </button>
                </div>

                <div className="relative">
                  <Search className="w-4 h-4 text-zinc-500 absolute left-3 top-1/2 -translate-y-1/2" />
                  <input
                    type="text"
                    value={query}
                    onChange={(e) => setQuery(e.target.value)}
                    placeholder="Search LRCLIB by title, artist, or album..."
                    className="w-full bg-white/[0.05] border border-white/[0.10] rounded-lg pl-9 pr-3 py-2 text-[13px] text-white placeholder:text-zinc-500 focus:outline-none focus:border-white/25"
                  />
                </div>

                <div className="flex-1 overflow-y-auto nova-scroll divide-y divide-white/[0.07]">
                  {searchResults.map((entry) => (
                    <div
                      key={entry.id}
                      className="py-3.5 flex items-center justify-between gap-3"
                    >
                      <div className="min-w-0">
                        <div className="text-[13.5px] font-medium text-white truncate">
                          {entry.trackName}
                        </div>
                        <div className="text-[12px] text-zinc-400 truncate mt-0.5">
                          {entry.artistName} · {entry.albumName}
                        </div>
                        <div className="text-[11px] font-mono-tabular text-zinc-500 mt-1">
                          {entry.source} · {entry.lines.length} lines · {formatTime(entry.duration)}
                        </div>
                      </div>
                      <button
                        type="button"
                        onClick={() => {
                          applyLrclibEntry(entry);
                          setSearchDrawerOpen(false);
                        }}
                        className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-[12px] font-medium bg-white/[0.10] hover:bg-white/[0.18] text-white whitespace-nowrap shrink-0 cursor-pointer"
                      >
                        <Check className="w-3.5 h-3.5" />
                        <span>Use LRC</span>
                      </button>
                    </div>
                  ))}
                </div>
              </motion.aside>
            )}
          </AnimatePresence>
        </motion.div>
      )}
    </AnimatePresence>
  );
}
