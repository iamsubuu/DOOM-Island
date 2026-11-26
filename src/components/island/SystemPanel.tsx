import React from 'react';
import {
  AudioLines,
  Battery,
  BatteryCharging,
  Camera,
  CameraOff,
  Check,
  Cpu,
  Headphones,
  MemoryStick,
  Mic,
  MicOff,
  Speaker,
  Sun,
  Volume2,
  VolumeX,
  Wifi,
  WifiOff,
} from 'lucide-react';
import { useNova } from '../../context/NovaContext';

export function SystemPanel() {
  const { system, updateSystem, settings, showToast } = useNova();
  const w = settings.widgets;

  const activeDevice =
    system.audioDevices.find((d) => d.id === system.activeAudioDeviceId) ?? system.audioDevices[0];

  return (
    <div className="flex flex-col gap-3.5">
      {/* Quick Hardware Toggles Row */}
      <div className="grid grid-cols-4 gap-2">
        {w.wifi && (
          <button
            type="button"
            onClick={() => updateSystem({ wifiConnected: !system.wifiConnected })}
            className={`flex flex-col items-start gap-1.5 p-2.5 rounded-xl border transition-colors text-left cursor-pointer ${
              system.wifiConnected
                ? 'bg-white/[0.09] border-white/[0.12] text-white'
                : 'bg-white/[0.02] border-white/[0.06] text-zinc-400 hover:bg-white/[0.05]'
            }`}
          >
            {system.wifiConnected ? (
              <Wifi className="w-4 h-4" style={{ color: 'var(--nova-accent)' }} />
            ) : (
              <WifiOff className="w-4 h-4" />
            )}
            <div className="min-w-0 w-full">
              <div className="text-[11.5px] font-medium truncate">Wi-Fi</div>
              <div className="text-[10.5px] text-zinc-400 truncate">
                {system.wifiConnected ? system.wifiNetwork : 'Disconnected'}
              </div>
            </div>
          </button>
        )}

        {w.battery && (
          <button
            type="button"
            onClick={() => updateSystem({ isCharging: !system.isCharging })}
            className="flex flex-col items-start gap-1.5 p-2.5 rounded-xl border bg-white/[0.05] border-white/[0.08] text-white hover:bg-white/[0.09] transition-colors text-left cursor-pointer"
            title="Toggle AC power simulation"
          >
            {system.isCharging ? (
              <BatteryCharging className="w-4 h-4 text-emerald-400" />
            ) : (
              <Battery className="w-4 h-4 text-zinc-300" />
            )}
            <div className="min-w-0 w-full">
              <div className="text-[11.5px] font-mono-tabular font-medium">
                {system.batteryLevel}%
              </div>
              <div className="text-[10.5px] text-zinc-400 truncate">
                {system.isCharging ? 'Charging' : 'On Battery'}
              </div>
            </div>
          </button>
        )}

        {w.microphone && (
          <button
            type="button"
            onClick={() => updateSystem({ micMuted: !system.micMuted })}
            className={`flex flex-col items-start gap-1.5 p-2.5 rounded-xl border transition-colors text-left cursor-pointer ${
              !system.micMuted
                ? 'bg-white/[0.09] border-white/[0.12] text-white'
                : 'bg-white/[0.02] border-white/[0.06] text-zinc-400 hover:bg-white/[0.05]'
            }`}
          >
            {system.micMuted ? (
              <MicOff className="w-4 h-4 text-rose-400" />
            ) : (
              <Mic className="w-4 h-4 text-emerald-400" />
            )}
            <div className="min-w-0 w-full">
              <div className="text-[11.5px] font-medium truncate">Microphone</div>
              <div className="text-[10.5px] text-zinc-400 truncate">
                {system.micMuted ? 'Muted' : 'Active'}
              </div>
            </div>
          </button>
        )}

        {w.camera && (
          <button
            type="button"
            onClick={() => updateSystem({ cameraActive: !system.cameraActive })}
            className={`flex flex-col items-start gap-1.5 p-2.5 rounded-xl border transition-colors text-left cursor-pointer ${
              system.cameraActive
                ? 'bg-white/[0.09] border-white/[0.12] text-white'
                : 'bg-white/[0.02] border-white/[0.06] text-zinc-400 hover:bg-white/[0.05]'
            }`}
          >
            {system.cameraActive ? (
              <Camera className="w-4 h-4 text-amber-400" />
            ) : (
              <CameraOff className="w-4 h-4" />
            )}
            <div className="min-w-0 w-full">
              <div className="text-[11.5px] font-medium truncate">Camera</div>
              <div className="text-[10.5px] text-zinc-400 truncate">
                {system.cameraActive ? 'In Use' : 'Standby'}
              </div>
            </div>
          </button>
        )}
      </div>

      {/* Volume & Brightness Sliders */}
      <div className="flex flex-col gap-2.5 py-2 border-y border-white/[0.08]">
        {w.volume && (
          <div className="flex items-center gap-3">
            <button
              type="button"
              onClick={() => updateSystem({ isMuted: !system.isMuted })}
              className="text-zinc-300 hover:text-white transition-colors cursor-pointer"
              aria-label={system.isMuted ? 'Unmute volume' : 'Mute volume'}
            >
              {system.isMuted || system.volume === 0 ? (
                <VolumeX className="w-4 h-4 text-zinc-500" />
              ) : (
                <Volume2 className="w-4 h-4" />
              )}
            </button>
            <span className="text-[11.5px] text-zinc-400 w-16 shrink-0">Volume</span>
            <input
              type="range"
              min={0}
              max={100}
              value={system.isMuted ? 0 : system.volume}
              onChange={(e) =>
                updateSystem({
                  volume: Number(e.target.value),
                  isMuted: Number(e.target.value) === 0,
                })
              }
              aria-label="System volume"
              className="nova-slider flex-1"
            />
            <span className="font-mono-tabular text-[11.5px] text-zinc-300 w-9 text-right">
              {system.isMuted ? 0 : system.volume}%
            </span>
          </div>
        )}

        {w.brightness && (
          <div className="flex items-center gap-3">
            <Sun className="w-4 h-4 text-zinc-300 shrink-0" />
            <span className="text-[11.5px] text-zinc-400 w-16 shrink-0">Display</span>
            <input
              type="range"
              min={35}
              max={100}
              value={system.brightness}
              onChange={(e) => updateSystem({ brightness: Number(e.target.value) })}
              aria-label="Display brightness"
              className="nova-slider flex-1"
            />
            <span className="font-mono-tabular text-[11.5px] text-zinc-300 w-9 text-right">
              {system.brightness}%
            </span>
          </div>
        )}
      </div>

      {/* Audio Output Devices Switcher */}
      {w.audioDevices && (
        <div className="flex flex-col gap-1.5">
          <div className="flex items-center justify-between text-[11px] text-zinc-400">
            <span>Audio Output Device</span>
            <span className="font-mono-tabular text-zinc-500">{activeDevice?.sampleRate}</span>
          </div>
          <div className="grid grid-cols-2 gap-1.5">
            {system.audioDevices.map((dev) => {
              const selected = dev.id === system.activeAudioDeviceId;
              const DevIcon =
                dev.kind === 'headphones'
                  ? Headphones
                  : dev.kind === 'interface'
                  ? AudioLines
                  : Speaker;
              return (
                <button
                  key={dev.id}
                  type="button"
                  onClick={() => {
                    updateSystem({ activeAudioDeviceId: dev.id });
                    showToast(`Audio output switched to ${dev.name}`);
                  }}
                  className={`flex items-center justify-between gap-2 px-2.5 py-1.5 rounded-lg border text-left transition-colors cursor-pointer ${
                    selected
                      ? 'bg-white/[0.11] border-white/[0.16] text-white'
                      : 'bg-white/[0.02] border-white/[0.06] text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.06]'
                  }`}
                >
                  <span className="flex items-center gap-2 min-w-0">
                    <DevIcon className="w-3.5 h-3.5 shrink-0" />
                    <span className="text-[11.5px] truncate">{dev.name}</span>
                  </span>
                  {selected && (
                    <Check
                      className="w-3.5 h-3.5 shrink-0"
                      style={{ color: 'var(--nova-accent)' }}
                    />
                  )}
                </button>
              );
            })}
          </div>
        </div>
      )}

      {/* CPU & RAM Telemetry */}
      {(w.cpu || w.ram) && (
        <div className="grid grid-cols-2 gap-3 pt-1">
          {w.cpu && (
            <div className="flex flex-col gap-1.5">
              <div className="flex items-center justify-between text-[11.5px]">
                <span className="flex items-center gap-1.5 text-zinc-300">
                  <Cpu className="w-3.5 h-3.5 text-zinc-400" />
                  <span>CPU Load</span>
                </span>
                <span className="font-mono-tabular text-white font-medium">{system.cpuUsage}%</span>
              </div>
              <div className="h-1.5 w-full rounded-full bg-white/[0.09] overflow-hidden">
                <div
                  className="h-full rounded-full transition-[width] duration-500"
                  style={{
                    width: `${system.cpuUsage}%`,
                    backgroundColor: system.cpuUsage > 70 ? '#f59e0b' : 'var(--nova-accent)',
                  }}
                />
              </div>
            </div>
          )}

          {w.ram && (
            <div className="flex flex-col gap-1.5">
              <div className="flex items-center justify-between text-[11.5px]">
                <span className="flex items-center gap-1.5 text-zinc-300">
                  <MemoryStick className="w-3.5 h-3.5 text-zinc-400" />
                  <span>Memory</span>
                </span>
                <span className="font-mono-tabular text-white font-medium">
                  {system.ramUsedGb.toFixed(1)} / {system.ramTotalGb} GB
                </span>
              </div>
              <div className="h-1.5 w-full rounded-full bg-white/[0.09] overflow-hidden">
                <div
                  className="h-full rounded-full transition-[width] duration-500 bg-zinc-200"
                  style={{ width: `${system.ramUsage}%` }}
                />
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
