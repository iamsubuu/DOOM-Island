import React from 'react';
import {
  ArrowDownToLine,
  Camera,
  CheckCircle2,
  Copy,
  FolderSync,
  Pause,
  Play,
  X,
} from 'lucide-react';
import { useNova } from '../../context/NovaContext';
import { SafeMediaImage } from '../ui/Controls';

export function ActivityPanel() {
  const {
    activities,
    toggleActivityPause,
    removeActivity,
    simulateDownload,
    simulateTransfer,
    simulateScreenshot,
    settings,
    showToast,
  } = useNova();

  const w = settings.widgets;
  const visibleActivities = activities.filter((act) => {
    if (act.type === 'download' && !w.downloads) return false;
    if (act.type === 'transfer' && !w.transfers) return false;
    if (act.type === 'screenshot' && !w.screenshots) return false;
    return true;
  });

  return (
    <div className="flex flex-col gap-3">
      {/* Trigger Simulation Strip */}
      <div className="flex items-center justify-between gap-2">
        <span className="text-[11.5px] font-medium text-zinc-300">Live Activities</span>
        <div className="flex items-center gap-1.5">
          {w.downloads && (
            <button
              type="button"
              onClick={simulateDownload}
              className="flex items-center gap-1 px-2 py-1 rounded-md text-[11px] text-zinc-300 bg-white/[0.06] hover:bg-white/[0.12] hover:text-white transition-colors whitespace-nowrap cursor-pointer"
            >
              <ArrowDownToLine className="w-3 h-3" />
              <span>Download</span>
            </button>
          )}
          {w.transfers && (
            <button
              type="button"
              onClick={simulateTransfer}
              className="flex items-center gap-1 px-2 py-1 rounded-md text-[11px] text-zinc-300 bg-white/[0.06] hover:bg-white/[0.12] hover:text-white transition-colors whitespace-nowrap cursor-pointer"
            >
              <FolderSync className="w-3 h-3" />
              <span>Transfer</span>
            </button>
          )}
          {w.screenshots && (
            <button
              type="button"
              onClick={simulateScreenshot}
              className="flex items-center gap-1 px-2 py-1 rounded-md text-[11px] text-zinc-300 bg-white/[0.06] hover:bg-white/[0.12] hover:text-white transition-colors whitespace-nowrap cursor-pointer"
            >
              <Camera className="w-3 h-3" />
              <span>Snip</span>
            </button>
          )}
        </div>
      </div>

      {/* Activity Feed */}
      <div className="max-h-56 overflow-y-auto nova-scroll divide-y divide-white/[0.06]">
        {visibleActivities.length === 0 ? (
          <div className="py-8 text-center text-[12px] text-zinc-500">
            No active downloads, transfers, or captures visible.
          </div>
        ) : (
          visibleActivities.map((act) => (
            <div key={act.id} className="py-2.5 flex flex-col gap-2">
              <div className="flex items-start justify-between gap-3">
                <div className="flex items-start gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-lg bg-white/[0.06] border border-white/[0.08] flex items-center justify-center shrink-0 mt-0.5 text-zinc-200">
                    {act.type === 'download' ? (
                      <ArrowDownToLine className="w-3.5 h-3.5" />
                    ) : act.type === 'transfer' ? (
                      <FolderSync className="w-3.5 h-3.5" />
                    ) : (
                      <Camera className="w-3.5 h-3.5" />
                    )}
                  </div>
                  <div className="min-w-0">
                    <div className="text-[12.5px] font-medium text-white truncate">
                      {act.title}
                    </div>
                    <div className="text-[11px] text-zinc-400 truncate">
                      {act.subtitle} · <span className="font-mono-tabular">{act.sizeText}</span>
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-1 shrink-0">
                  {act.type !== 'screenshot' && act.status !== 'completed' && (
                    <button
                      type="button"
                      onClick={() => toggleActivityPause(act.id)}
                      className="p-1.5 rounded-md text-zinc-400 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
                      aria-label={act.status === 'active' ? 'Pause transfer' : 'Resume transfer'}
                    >
                      {act.status === 'active' ? (
                        <Pause className="w-3.5 h-3.5" />
                      ) : (
                        <Play className="w-3.5 h-3.5" />
                      )}
                    </button>
                  )}
                  <button
                    type="button"
                    onClick={() => removeActivity(act.id)}
                    className="p-1.5 rounded-md text-zinc-500 hover:text-zinc-200 hover:bg-white/[0.08] transition-colors cursor-pointer"
                    aria-label="Remove activity"
                  >
                    <X className="w-3.5 h-3.5" />
                  </button>
                </div>
              </div>

              {act.type === 'screenshot' ? (
                <div className="flex items-center gap-3 pl-9">
                  <SafeMediaImage
                    src={act.imageUrl}
                    alt={act.title}
                    fallbackKind="preview"
                    className="w-24 h-14 rounded-lg object-cover border border-white/10 shrink-0"
                  />
                  <div className="flex items-center gap-1.5 flex-wrap">
                    <button
                      type="button"
                      onClick={() => showToast('Screenshot copied to clipboard')}
                      className="flex items-center gap-1 px-2.5 py-1 rounded-md text-[11px] font-medium bg-white/[0.09] hover:bg-white/[0.16] text-white transition-colors cursor-pointer"
                    >
                      <Copy className="w-3 h-3" />
                      <span>Copy</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => showToast('Saved to Pictures\\Screenshots')}
                      className="px-2.5 py-1 rounded-md text-[11px] font-medium bg-white/[0.05] hover:bg-white/[0.11] text-zinc-300 transition-colors cursor-pointer"
                    >
                      Save PNG
                    </button>
                  </div>
                </div>
              ) : (
                <div className="pl-9 flex flex-col gap-1">
                  <div className="h-1.5 w-full rounded-full bg-white/[0.08] overflow-hidden">
                    <div
                      className="h-full rounded-full transition-[width] duration-300"
                      style={{
                        width: `${act.progress}%`,
                        backgroundColor:
                          act.status === 'completed'
                            ? '#34d399'
                            : act.status === 'paused'
                            ? '#a1a1aa'
                            : 'var(--nova-accent)',
                      }}
                    />
                  </div>
                  <div className="flex items-center justify-between text-[10.5px] font-mono-tabular text-zinc-400">
                    <span className="flex items-center gap-1">
                      {act.status === 'completed' ? (
                        <>
                          <CheckCircle2 className="w-3 h-3 text-emerald-400" />
                          <span className="text-emerald-400">Completed</span>
                        </>
                      ) : act.status === 'paused' ? (
                        'Paused'
                      ) : (
                        act.speed
                      )}
                    </span>
                    <span>{act.progress}%</span>
                  </div>
                </div>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
