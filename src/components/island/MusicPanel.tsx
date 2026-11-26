import React from 'react';
import {
  Disc3,
  Maximize2,
  MicVocal,
  Pause,
  Play,
  Repeat,
  Shuffle,
  SkipBack,
  SkipForward,
  Volume2,
  VolumeX,
} from 'lucide-react';
import { formatTime, useNova } from '../../context/NovaContext';
import { SafeMediaImage } from '../ui/Controls';
import { AudioEqualizer } from './AudioEqualizer';

export function MusicPanel() {
  const {
    tracks,
    currentTrack,
    currentTrackIndex,
    isPlaying,
    currentTime,
    shuffle,
    repeat,
    togglePlay,
    nextTrack,
    prevTrack,
    seekTo,
    selectTrack,
    setShuffle,
    setRepeat,
    setIslandMode,
    setFullscreenLyricsOpen,
    system,
    updateSystem,
    settings,
  } = useNova();

  const progressPct =
    currentTrack.duration > 0 ? Math.min(100, (currentTime / currentTrack.duration) * 100) : 0;

  return (
    <div className="flex flex-col gap-4">
      {/* Main Track Row */}
      <div className="flex items-center gap-4">
        <div className="relative w-16 h-16 rounded-2xl overflow-hidden shrink-0 border border-white/10 shadow-lg">
          <SafeMediaImage
            src={currentTrack.artwork}
            alt={`${currentTrack.title} album artwork`}
            className="w-full h-full object-cover"
          />
        </div>

        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2 text-[11px] text-zinc-400 mb-1">
            <Disc3
              className="w-3.5 h-3.5 shrink-0"
              style={{ color: settings.spotify.connected ? '#1DB954' : undefined }}
            />
            <span className="truncate">
              {settings.spotify.connected ? 'Spotify (Mock)' : 'Local Audio'}
            </span>
            <span aria-hidden="true">·</span>
            <span>{isPlaying ? 'Playing' : 'Paused'}</span>
            <span aria-hidden="true">·</span>
            <span className="truncate text-zinc-500">{settings.spotify.activeDevice}</span>
          </div>

          <h3 className="text-[15px] font-semibold text-white truncate leading-tight">
            {currentTrack.title}
          </h3>
          <p className="text-[12.5px] text-zinc-400 truncate mt-0.5">
            {currentTrack.artist} · {currentTrack.album}
          </p>
        </div>

        {/* Audio-Reactive Equalizer & Lyrics Actions */}
        <div className="flex flex-col items-end gap-2 shrink-0">
          {settings.music.showAudioVisualizer && (
            <AudioEqualizer
              isPlaying={isPlaying}
              bpm={currentTrack.bpm}
              bars={8}
              height={16}
              reduceMotion={settings.animation.reduceMotion}
            />
          )}
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => setIslandMode('lyrics')}
              title="Synchronized Lyrics"
              className="flex items-center gap-1.5 px-2.5 py-1 rounded-lg text-[11.5px] font-medium text-zinc-300 bg-white/[0.07] hover:bg-white/[0.14] hover:text-white transition-colors whitespace-nowrap cursor-pointer"
            >
              <MicVocal className="w-3.5 h-3.5" />
              <span>Lyrics</span>
            </button>
            <button
              type="button"
              onClick={() => setFullscreenLyricsOpen(true)}
              title="Fullscreen Lyrics"
              className="p-1.5 rounded-lg text-zinc-400 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
              aria-label="Open Fullscreen Lyrics"
            >
              <Maximize2 className="w-3.5 h-3.5" />
            </button>
          </div>
        </div>
      </div>

      {/* Interactive Scrubbing Progress Bar */}
      <div className="flex flex-col gap-1.5">
        <div className="relative flex items-center group">
          <div className="w-full h-1.5 rounded-full bg-white/[0.12] overflow-hidden">
            <div
              className="h-full rounded-full transition-[width] duration-200"
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
            aria-label="Seek track position"
            className="absolute inset-0 w-full h-full opacity-0 cursor-pointer"
          />
        </div>
        <div className="flex items-center justify-between text-[11px] font-mono-tabular text-zinc-400">
          <span>{formatTime(currentTime)}</span>
          <span>
            -{formatTime(Math.max(0, currentTrack.duration - currentTime))} /{' '}
            {formatTime(currentTrack.duration)}
          </span>
        </div>
      </div>

      {/* Transport Controls & Volume */}
      <div className="flex items-center justify-between gap-4 pt-0.5">
        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={() => setShuffle((s) => !s)}
            className={`p-2 rounded-lg transition-colors cursor-pointer ${
              shuffle
                ? 'text-white bg-white/[0.12]'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.06]'
            }`}
            aria-label="Toggle shuffle"
            title="Shuffle"
          >
            <Shuffle className="w-3.5 h-3.5" />
          </button>
          <button
            type="button"
            onClick={prevTrack}
            className="p-2 rounded-lg text-zinc-200 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
            aria-label="Previous track"
          >
            <SkipBack className="w-4 h-4 fill-current" />
          </button>
          <button
            type="button"
            onClick={togglePlay}
            className="w-9 h-9 rounded-full bg-white text-zinc-950 flex items-center justify-center hover:scale-[1.04] active:scale-[0.97] transition-transform cursor-pointer mx-1 shadow-md"
            aria-label={isPlaying ? 'Pause' : 'Play'}
          >
            {isPlaying ? (
              <Pause className="w-4 h-4 fill-current" />
            ) : (
              <Play className="w-4 h-4 fill-current ml-0.5" />
            )}
          </button>
          <button
            type="button"
            onClick={nextTrack}
            className="p-2 rounded-lg text-zinc-200 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
            aria-label="Next track"
          >
            <SkipForward className="w-4 h-4 fill-current" />
          </button>
          <button
            type="button"
            onClick={() => setRepeat((r) => !r)}
            className={`p-2 rounded-lg transition-colors cursor-pointer ${
              repeat
                ? 'text-white bg-white/[0.12]'
                : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.06]'
            }`}
            aria-label="Toggle repeat"
            title="Repeat"
          >
            <Repeat className="w-3.5 h-3.5" />
          </button>
        </div>

        {/* Inline Volume */}
        <div className="flex items-center gap-2 w-32">
          <button
            type="button"
            onClick={() => updateSystem({ isMuted: !system.isMuted })}
            className="text-zinc-400 hover:text-white transition-colors cursor-pointer"
            aria-label={system.isMuted ? 'Unmute' : 'Mute'}
          >
            {system.isMuted || system.volume === 0 ? (
              <VolumeX className="w-3.5 h-3.5" />
            ) : (
              <Volume2 className="w-3.5 h-3.5" />
            )}
          </button>
          <input
            type="range"
            min={0}
            max={100}
            value={system.isMuted ? 0 : system.volume}
            onChange={(e) =>
              updateSystem({ volume: Number(e.target.value), isMuted: Number(e.target.value) === 0 })
            }
            aria-label="Output volume"
            className="nova-slider flex-1"
          />
        </div>
      </div>

      {/* Quick Queue Switcher */}
      <div className="pt-2.5 border-t border-white/[0.08] flex items-center justify-between gap-2">
        <span className="text-[11px] text-zinc-500 whitespace-nowrap shrink-0">Queue</span>
        <div className="flex items-center gap-1.5 overflow-x-auto nova-scroll">
          {tracks.map((track, idx) => (
            <button
              key={track.id}
              type="button"
              onClick={() => selectTrack(idx)}
              className={`px-2.5 py-1 rounded-md text-[11.5px] transition-colors whitespace-nowrap shrink-0 cursor-pointer ${
                idx === currentTrackIndex
                  ? 'bg-white/[0.14] text-white font-medium'
                  : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.06]'
              }`}
            >
              {track.title}
            </button>
          ))}
        </div>
      </div>
    </div>
  );
}
