import React, { useRef } from 'react';
import { Monitor } from 'lucide-react';
import { useNova } from '../../context/NovaContext';

/**
 * Interactive Live Positioning Preview canvas inside Settings > Position & Display.
 * Clicking or dragging anywhere along the top area switches to Custom X/Y and
 * moves the real floating Dynamic Island immediately.
 */
export function PositionPreview() {
  const { settings, updateSettingsSection, appShortName } = useNova();
  const canvasRef = useRef<HTMLDivElement | null>(null);
  const pos = settings.position;

  const updateFromPointer = (clientX: number, clientY: number) => {
    const rect = canvasRef.current?.getBoundingClientRect();
    if (!rect) return;
    const relX = Math.round(((clientX - rect.left) / rect.width) * 100);
    const relY = Math.round(((clientY - rect.top) / rect.height) * 180);
    updateSettingsSection('position', {
      screenPosition: 'custom',
      customX: Math.max(12, Math.min(88, relX)),
      customY: Math.max(8, Math.min(140, relY)),
    });
  };

  const handlePointerDown = (e: React.PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId);
    updateFromPointer(e.clientX, e.clientY);
  };

  const handlePointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    if (e.buttons !== 1) return;
    updateFromPointer(e.clientX, e.clientY);
  };

  const pillLeftPct =
    pos.screenPosition === 'top-left'
      ? 18
      : pos.screenPosition === 'top-right'
      ? 82
      : pos.screenPosition === 'top-center'
      ? 50
      : Math.max(12, Math.min(88, pos.customX));

  const pillTopPct =
    pos.screenPosition === 'custom'
      ? Math.max(8, Math.min(72, Math.round((pos.customY / 180) * 80)))
      : 10;

  return (
    <div className="mb-4 rounded-xl border border-zinc-200/90 dark:border-white/[0.08] bg-zinc-100/80 dark:bg-white/[0.025] p-4 flex flex-col gap-3">
      <div className="flex items-center justify-between gap-2 text-[12px]">
        <span className="flex items-center gap-1.5 font-medium text-zinc-800 dark:text-zinc-200">
          <Monitor className="w-3.5 h-3.5 text-zinc-500 dark:text-zinc-400" />
          <span>Live Positioning Preview</span>
        </span>
        <span className="font-mono-tabular text-[11px] text-zinc-500 dark:text-zinc-400">
          {pos.screenPosition === 'custom'
            ? `X ${pos.customX}% · Y ${pos.customY}px`
            : pos.screenPosition.replace('-', ' ')}
        </span>
      </div>

      {/* Simulated Display Canvas */}
      <div
        ref={canvasRef}
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        role="slider"
        aria-label="Drag or click to position island on monitor"
        aria-valuenow={pillLeftPct}
        tabIndex={0}
        className="relative h-36 w-full rounded-xl overflow-hidden border border-zinc-300/90 dark:border-white/[0.10] bg-gradient-to-br from-zinc-200 via-zinc-100 to-zinc-200 dark:from-[#0A0C12] dark:via-[#12151E] dark:to-[#090B10] cursor-crosshair select-none"
      >
        {/* Subtle alignment guide lines */}
        <div className="absolute inset-y-0 left-1/2 w-px bg-zinc-400/25 dark:bg-white/[0.06] pointer-events-none" />
        <div className="absolute inset-x-0 top-[18px] h-px bg-zinc-400/20 dark:bg-white/[0.05] pointer-events-none" />

        {/* Simulated Pure-Black Dynamic Island Pill */}
        <div
          className="absolute -translate-x-1/2 -translate-y-1/2 h-6 px-3 rounded-full bg-black border border-white/15 shadow-lg flex items-center gap-1.5 transition-all duration-150 pointer-events-none"
          style={{
            left: `${pillLeftPct}%`,
            top: `${pillTopPct}%`,
          }}
        >
          <span
            className="w-1.5 h-1.5 rounded-full"
            style={{ backgroundColor: 'var(--nova-accent)' }}
          />
          <span className="text-[9.5px] font-medium text-white tracking-tight whitespace-nowrap">
            {appShortName}
          </span>
        </div>

        {/* Simulated Bottom Taskbar Strip */}
        <div className="absolute bottom-0 inset-x-0 h-3 bg-white/70 dark:bg-black/60 border-t border-zinc-300/60 dark:border-white/[0.07] flex items-center justify-center gap-1 pointer-events-none">
          <span className="w-2 h-1 rounded-full bg-zinc-400/70 dark:bg-white/25" />
          <span className="w-2 h-1 rounded-full bg-zinc-400/70 dark:bg-white/25" />
          <span className="w-2 h-1 rounded-full bg-zinc-400/70 dark:bg-white/25" />
        </div>
      </div>

      <p className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
        Click or drag anywhere on the display canvas to place the island at custom X/Y coordinates.
      </p>
    </div>
  );
}
