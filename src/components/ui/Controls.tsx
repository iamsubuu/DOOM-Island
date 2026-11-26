import React, { useState } from 'react';
import { Disc3, Image as ImageIcon } from 'lucide-react';

/**
 * Reusable Toggle Switch (maps directly to WPF ToggleSwitch) — Dual-theme ready
 */
export function NovaSwitch({
  checked,
  onChange,
  ariaLabel,
  disabled = false,
}: {
  checked: boolean;
  onChange: (checked: boolean) => void;
  ariaLabel: string;
  disabled?: boolean;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      disabled={disabled}
      onClick={() => !disabled && onChange(!checked)}
      className={`relative inline-flex h-[22px] w-[40px] shrink-0 items-center rounded-full border transition-colors duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-zinc-400/50 ${
        disabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer'
      } ${
        checked
          ? 'border-transparent'
          : 'border-zinc-300 dark:border-white/15 bg-zinc-200/80 dark:bg-white/[0.06] hover:bg-zinc-300/80 dark:hover:bg-white/[0.11]'
      }`}
      style={checked ? { backgroundColor: 'var(--nova-accent)' } : undefined}
    >
      <span
        className={`pointer-events-none block h-[14px] w-[14px] rounded-full bg-white shadow-sm transition-transform duration-150 ${
          checked ? 'translate-x-[21px]' : 'translate-x-[3px]'
        }`}
      />
    </button>
  );
}

/**
 * Reusable Setting Row with clean hairline divider (no nested boxes) — Dual-theme ready
 */
export function SettingRow({
  title,
  description,
  children,
}: {
  title: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 sm:gap-6 py-3.5 border-b border-zinc-200/80 dark:border-white/[0.06] last:border-b-0">
      <div className="min-w-0">
        <div className="text-[13.5px] font-medium text-zinc-900 dark:text-zinc-100 leading-snug">
          {title}
        </div>
        {description && (
          <p className="text-[12px] text-zinc-500 dark:text-zinc-400 mt-0.5 leading-relaxed">
            {description}
          </p>
        )}
      </div>
      <div className="shrink-0 flex items-center gap-3">{children}</div>
    </div>
  );
}

/**
 * Reusable Slider Row — Dual-theme ready
 */
export function SettingSlider({
  title,
  description,
  value,
  min,
  max,
  step = 1,
  unit = '',
  onChange,
}: {
  title: string;
  description?: string;
  value: number;
  min: number;
  max: number;
  step?: number;
  unit?: string;
  onChange: (value: number) => void;
}) {
  return (
    <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 py-3.5 border-b border-zinc-200/80 dark:border-white/[0.06] last:border-b-0">
      <div className="min-w-0">
        <div className="text-[13.5px] font-medium text-zinc-900 dark:text-zinc-100 leading-snug">
          {title}
        </div>
        {description && (
          <p className="text-[12px] text-zinc-500 dark:text-zinc-400 mt-0.5 leading-relaxed">
            {description}
          </p>
        )}
      </div>
      <div className="flex items-center gap-3 shrink-0 w-full sm:w-56">
        <input
          type="range"
          min={min}
          max={max}
          step={step}
          value={value}
          onChange={(e) => onChange(Number(e.target.value))}
          className="nova-slider flex-1"
          aria-label={title}
        />
        <span className="font-mono-tabular text-[12px] text-zinc-700 dark:text-zinc-300 w-16 text-right">
          {step < 1 ? value.toFixed(1) : value}
          {unit}
        </span>
      </div>
    </div>
  );
}

/**
 * Resilient Image component with automatic stylized fallback (Zero-Broken-Image policy)
 */
export function SafeMediaImage({
  src,
  alt,
  className = '',
  fallbackKind = 'album',
}: {
  src?: string;
  alt: string;
  className?: string;
  fallbackKind?: 'album' | 'preview';
}) {
  const [failed, setFailed] = useState(false);

  if (!src || failed) {
    return (
      <div
        className={`flex items-center justify-center bg-gradient-to-br from-zinc-800 to-zinc-950 text-zinc-500 ${className}`}
        aria-label={alt}
      >
        {fallbackKind === 'album' ? (
          <Disc3 className="w-1/3 h-1/3 opacity-70" />
        ) : (
          <ImageIcon className="w-1/4 h-1/4 opacity-70" />
        )}
      </div>
    );
  }

  return (
    <img
      src={src}
      alt={alt}
      referrerPolicy="no-referrer"
      onError={() => setFailed(true)}
      className={className}
    />
  );
}
