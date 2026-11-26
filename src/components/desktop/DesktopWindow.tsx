import React, { useState } from 'react';
import { motion } from 'motion/react';
import { Maximize2, Minimize2, Minus, X } from 'lucide-react';
import type { DesktopWindowId } from '../../types/island';

interface DesktopWindowProps {
  id: DesktopWindowId;
  title: string;
  subtitle?: string;
  icon: React.ReactNode;
  isActive: boolean;
  onFocus: () => void;
  onClose: () => void;
  defaultWidth?: number;
  defaultHeight?: number;
  offsetX?: number;
  offsetY?: number;
  children: React.ReactNode;
}

/**
 * Reusable Minimalist Desktop Application Window Chrome
 * Dual-theme ready (Dark default + Light mode). Maps cleanly to WPF WindowChrome.
 */
export function DesktopWindow({
  title,
  subtitle,
  icon,
  isActive,
  onFocus,
  onClose,
  defaultWidth = 1040,
  defaultHeight = 640,
  offsetX = 0,
  offsetY = 0,
  children,
}: DesktopWindowProps) {
  const [maximized, setMaximized] = useState(false);

  return (
    <motion.div
      initial={{ opacity: 0, scale: 0.975, y: 10 }}
      animate={{ opacity: 1, scale: 1, y: 0 }}
      exit={{ opacity: 0, scale: 0.975, y: 8 }}
      transition={{ duration: 0.18, ease: [0.16, 1, 0.3, 1] }}
      onMouseDown={onFocus}
      className={`fixed flex flex-col overflow-hidden bg-[#FAFAFC]/96 dark:bg-[#101116]/96 backdrop-blur-xl border transition-shadow ${
        isActive
          ? 'border-zinc-300/90 dark:border-white/[0.12] shadow-[0_28px_72px_-16px_rgba(0,0,0,0.28)] dark:shadow-[0_32px_84px_-16px_rgba(0,0,0,0.88)]'
          : 'border-zinc-200/80 dark:border-white/[0.06] shadow-[0_16px_44px_-14px_rgba(0,0,0,0.18)] dark:shadow-[0_18px_50px_-14px_rgba(0,0,0,0.65)]'
      } ${maximized ? 'inset-x-3 top-[64px] bottom-[62px] rounded-2xl' : 'rounded-2xl'}`}
      style={
        maximized
          ? { zIndex: isActive ? 40 : 30 }
          : {
              zIndex: isActive ? 40 : 30,
              width: `min(${defaultWidth}px, calc(100vw - 32px))`,
              height: `min(${defaultHeight}px, calc(100vh - 148px))`,
              left: `calc(50% + ${offsetX}px)`,
              top: `calc(50% + ${offsetY}px)`,
              transform: 'translate(-50%, -50%)',
            }
      }
    >
      {/* Clean Minimalist Desktop Title Bar */}
      <div
        onDoubleClick={() => setMaximized((m) => !m)}
        className="h-11 shrink-0 px-4 flex items-center justify-between gap-4 border-b border-zinc-200/80 dark:border-white/[0.07] bg-zinc-100/60 dark:bg-white/[0.015] select-none"
      >
        <div className="flex items-center gap-2.5 min-w-0">
          <span className="text-zinc-700 dark:text-zinc-200 shrink-0">{icon}</span>
          <span className="text-[13px] font-semibold text-zinc-900 dark:text-zinc-100 whitespace-nowrap truncate">
            {title}
          </span>
          {subtitle && (
            <>
              <span className="text-zinc-400 dark:text-zinc-600 hidden sm:inline" aria-hidden="true">
                ·
              </span>
              <span className="text-[12px] text-zinc-500 dark:text-zinc-400 truncate hidden sm:inline">
                {subtitle}
              </span>
            </>
          )}
        </div>

        {/* Native Caption Buttons */}
        <div className="flex items-center gap-1 shrink-0">
          <button
            type="button"
            onClick={onClose}
            className="w-8 h-7 rounded-md flex items-center justify-center text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-200/70 dark:hover:bg-white/[0.08] transition-colors cursor-pointer"
            aria-label={`Minimize ${title}`}
            title="Minimize to Taskbar"
          >
            <Minus className="w-3.5 h-3.5" />
          </button>
          <button
            type="button"
            onClick={() => setMaximized((m) => !m)}
            className="w-8 h-7 rounded-md flex items-center justify-center text-zinc-500 dark:text-zinc-400 hover:text-zinc-900 dark:hover:text-white hover:bg-zinc-200/70 dark:hover:bg-white/[0.08] transition-colors cursor-pointer"
            aria-label={maximized ? `Restore ${title}` : `Maximize ${title}`}
            title={maximized ? 'Restore' : 'Maximize'}
          >
            {maximized ? (
              <Minimize2 className="w-3.5 h-3.5" />
            ) : (
              <Maximize2 className="w-3.5 h-3.5" />
            )}
          </button>
          <button
            type="button"
            onClick={onClose}
            className="w-8 h-7 rounded-md flex items-center justify-center text-zinc-500 dark:text-zinc-400 hover:text-white hover:bg-rose-600/85 transition-colors cursor-pointer"
            aria-label={`Close ${title}`}
            title="Close"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* Window Content Viewport */}
      <div className="flex-1 min-h-0 flex overflow-hidden">{children}</div>
    </motion.div>
  );
}
