import React from 'react';
import { Flag, Pause, Play, RotateCcw } from 'lucide-react';
import { formatStopwatch, formatTime, useNova } from '../../context/NovaContext';
import type { PomodoroPhase, TimerTab } from '../../types/island';

export function TimerPanel() {
  const {
    timer,
    setTimerTab,
    toggleCountdown,
    resetCountdown,
    setCountdownPreset,
    toggleStopwatch,
    resetStopwatch,
    lapStopwatch,
    togglePomodoro,
    resetPomodoro,
    setPomodoroPhase,
    settings,
  } = useNova();

  const radius = 38;
  const circumference = 2 * Math.PI * radius;

  const progressFraction =
    timer.activeTab === 'countdown'
      ? timer.countdownDuration > 0
        ? timer.countdownRemaining / timer.countdownDuration
        : 0
      : timer.activeTab === 'pomodoro'
      ? timer.pomodoroDuration > 0
        ? timer.pomodoroRemaining / timer.pomodoroDuration
        : 0
      : ((timer.stopwatchElapsed % 600) / 600);

  const dashOffset = circumference * (1 - progressFraction);

  const isRunning =
    timer.activeTab === 'countdown'
      ? timer.countdownRunning
      : timer.activeTab === 'stopwatch'
      ? timer.stopwatchRunning
      : timer.pomodoroRunning;

  const displayTime =
    timer.activeTab === 'countdown'
      ? formatTime(timer.countdownRemaining)
      : timer.activeTab === 'stopwatch'
      ? formatStopwatch(timer.stopwatchElapsed)
      : formatTime(timer.pomodoroRemaining);

  const handleToggle = () => {
    if (timer.activeTab === 'countdown') toggleCountdown();
    else if (timer.activeTab === 'stopwatch') toggleStopwatch();
    else togglePomodoro();
  };

  const handleReset = () => {
    if (timer.activeTab === 'countdown') resetCountdown();
    else if (timer.activeTab === 'stopwatch') resetStopwatch();
    else resetPomodoro();
  };

  return (
    <div className="flex flex-col gap-3.5">
      {/* Mode Switcher */}
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-1 p-0.5 bg-white/[0.05] rounded-lg">
          {(
            [
              ['pomodoro', 'Pomodoro'],
              ['countdown', 'Countdown'],
              ['stopwatch', 'Stopwatch'],
            ] as [TimerTab, string][]
          ).map(([id, label]) => (
            <button
              key={id}
              type="button"
              onClick={() => setTimerTab(id)}
              className={`px-2.5 py-1 rounded-md text-[11.5px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
                timer.activeTab === id
                  ? 'bg-white/[0.12] text-white'
                  : 'text-zinc-400 hover:text-zinc-200'
              }`}
            >
              {label}
            </button>
          ))}
        </div>

        <span className="text-[11px] text-zinc-400 font-mono-tabular whitespace-nowrap">
          {timer.activeTab === 'pomodoro'
            ? `Session ${timer.pomodoroSessionsCompleted + 1}`
            : timer.activeTab === 'stopwatch'
            ? `${timer.stopwatchLaps.length} Laps`
            : isRunning
            ? 'Active'
            : 'Ready'}
        </span>
      </div>

      {/* Ring + Controls Row */}
      <div className="flex items-center gap-5 py-1">
        {/* Progress Ring */}
        <div className="relative w-24 h-24 shrink-0 flex items-center justify-center">
          <svg className="w-24 h-24 -rotate-90" viewBox="0 0 92 92">
            <circle
              cx="46"
              cy="46"
              r={radius}
              fill="none"
              stroke="rgba(255,255,255,0.09)"
              strokeWidth="4.5"
            />
            <circle
              cx="46"
              cy="46"
              r={radius}
              fill="none"
              stroke="var(--nova-accent)"
              strokeWidth="4.5"
              strokeLinecap="round"
              strokeDasharray={circumference}
              strokeDashoffset={dashOffset}
              style={{
                transition: settings.animation.smoothProgressRings
                  ? 'stroke-dashoffset 260ms ease-out'
                  : 'none',
              }}
            />
          </svg>
          <div className="absolute inset-0 flex flex-col items-center justify-center">
            <span className="font-mono-tabular text-[17px] font-semibold text-white tracking-tight">
              {displayTime}
            </span>
            <span className="text-[10px] text-zinc-400 mt-0.5">
              {timer.activeTab === 'pomodoro'
                ? timer.pomodoroPhase === 'focus'
                  ? 'Focus'
                  : timer.pomodoroPhase === 'shortBreak'
                  ? 'Short Break'
                  : 'Long Break'
                : timer.activeTab === 'stopwatch'
                ? 'Elapsed'
                : 'Remaining'}
            </span>
          </div>
        </div>

        {/* Right-hand Presets & Actions */}
        <div className="flex-1 min-w-0 flex flex-col gap-3">
          {timer.activeTab === 'pomodoro' && (
            <div className="flex items-center gap-1.5 flex-wrap">
              {(
                [
                  ['focus', 'Focus 25m'],
                  ['shortBreak', 'Break 5m'],
                  ['longBreak', 'Long 15m'],
                ] as [PomodoroPhase, string][]
              ).map(([phase, label]) => (
                <button
                  key={phase}
                  type="button"
                  onClick={() => setPomodoroPhase(phase)}
                  className={`px-2.5 py-1 rounded-md text-[11px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
                    timer.pomodoroPhase === phase
                      ? 'bg-white/[0.13] text-white'
                      : 'bg-white/[0.04] text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.08]'
                  }`}
                >
                  {label}
                </button>
              ))}
            </div>
          )}

          {timer.activeTab === 'countdown' && (
            <div className="flex items-center gap-1.5 flex-wrap">
              {[
                { sec: 180, label: '3m' },
                { sec: 300, label: '5m' },
                { sec: 600, label: '10m' },
                { sec: 1500, label: '25m' },
              ].map((preset) => (
                <button
                  key={preset.sec}
                  type="button"
                  onClick={() => setCountdownPreset(preset.sec)}
                  className={`px-2.5 py-1 rounded-md text-[11px] font-mono-tabular transition-colors cursor-pointer ${
                    timer.countdownDuration === preset.sec
                      ? 'bg-white/[0.13] text-white font-medium'
                      : 'bg-white/[0.04] text-zinc-400 hover:text-zinc-200 hover:bg-white/[0.08]'
                  }`}
                >
                  {preset.label}
                </button>
              ))}
            </div>
          )}

          {timer.activeTab === 'stopwatch' && (
            <div className="min-h-[30px] max-h-14 overflow-y-auto nova-scroll flex flex-wrap gap-x-3 gap-y-1 text-[11px] font-mono-tabular text-zinc-400">
              {timer.stopwatchLaps.length === 0 ? (
                <span className="text-zinc-500">Press Lap while running to record splits.</span>
              ) : (
                timer.stopwatchLaps.map((lap, i) => (
                  <span key={`${lap}-${i}`}>
                    L{timer.stopwatchLaps.length - i}: {formatStopwatch(lap)}
                  </span>
                ))
              )}
            </div>
          )}

          {/* Action Buttons */}
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleToggle}
              className="flex items-center gap-1.5 px-4 py-1.5 rounded-lg text-[12px] font-semibold bg-white text-zinc-950 hover:bg-zinc-200 transition-colors cursor-pointer whitespace-nowrap"
            >
              {isRunning ? <Pause className="w-3.5 h-3.5 fill-current" /> : <Play className="w-3.5 h-3.5 fill-current" />}
              <span>{isRunning ? 'Pause' : 'Start'}</span>
            </button>

            {timer.activeTab === 'stopwatch' && (
              <button
                type="button"
                onClick={lapStopwatch}
                disabled={!timer.stopwatchRunning}
                className="flex items-center gap-1 px-3 py-1.5 rounded-lg text-[12px] font-medium bg-white/[0.07] text-zinc-200 hover:bg-white/[0.13] disabled:opacity-40 transition-colors cursor-pointer whitespace-nowrap"
              >
                <Flag className="w-3.5 h-3.5" />
                <span>Lap</span>
              </button>
            )}

            <button
              type="button"
              onClick={handleReset}
              className="flex items-center gap-1 px-3 py-1.5 rounded-lg text-[12px] font-medium bg-white/[0.06] text-zinc-300 hover:text-white hover:bg-white/[0.12] transition-colors cursor-pointer whitespace-nowrap"
            >
              <RotateCcw className="w-3.5 h-3.5" />
              <span>Reset</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
