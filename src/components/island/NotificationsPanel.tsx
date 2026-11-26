import React, { useMemo, useState } from 'react';
import {
  BellOff,
  CheckCheck,
  Code2,
  GitPullRequest,
  Hash,
  Mail,
  MessageSquare,
  Plus,
  Shield,
  X,
} from 'lucide-react';
import { useNova } from '../../context/NovaContext';
import type { NotificationItem } from '../../types/island';

export function AppBrandIcon({ appName }: { appName: NotificationItem['appName'] }) {
  const base = 'w-7 h-7 rounded-lg flex items-center justify-center shrink-0 border border-white/10';
  switch (appName) {
    case 'Slack':
      return (
        <div className={`${base} bg-zinc-900 text-zinc-100`}>
          <Hash className="w-3.5 h-3.5" />
        </div>
      );
    case 'GitHub':
      return (
        <div className={`${base} bg-zinc-900 text-zinc-100`}>
          <GitPullRequest className="w-3.5 h-3.5" />
        </div>
      );
    case 'Outlook':
      return (
        <div className={`${base} bg-zinc-900 text-sky-300`}>
          <Mail className="w-3.5 h-3.5" />
        </div>
      );
    case 'VS Code':
      return (
        <div className={`${base} bg-zinc-900 text-blue-300`}>
          <Code2 className="w-3.5 h-3.5" />
        </div>
      );
    case 'Discord':
      return (
        <div className={`${base} bg-zinc-900 text-indigo-300`}>
          <MessageSquare className="w-3.5 h-3.5" />
        </div>
      );
    default:
      return (
        <div className={`${base} bg-zinc-900 text-zinc-200`}>
          <Shield className="w-3.5 h-3.5" />
        </div>
      );
  }
}

export function NotificationsPanel() {
  const {
    notifications,
    dismissNotification,
    markAllNotificationsRead,
    clearNotifications,
    simulateIncomingNotification,
    settings,
    showToast,
  } = useNova();

  const [view, setView] = useState<'active' | 'history'>('active');

  const allowedNotifications = useMemo(
    () => notifications.filter((n) => settings.notifications.enabledApps[n.appName] !== false),
    [notifications, settings.notifications.enabledApps]
  );

  const visibleItems = useMemo(() => {
    if (!settings.notifications.enabled) return [];
    if (view === 'history') return allowedNotifications;
    return allowedNotifications.filter((n) => !n.dismissed);
  }, [allowedNotifications, view, settings.notifications.enabled]);

  return (
    <div className="flex flex-col gap-3">
      {/* Header Controls */}
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-1 p-0.5 bg-white/[0.05] rounded-lg">
          <button
            type="button"
            onClick={() => setView('active')}
            className={`px-2.5 py-1 rounded-md text-[11.5px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
              view === 'active' ? 'bg-white/[0.12] text-white' : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            Active ({allowedNotifications.filter((n) => !n.dismissed).length})
          </button>
          <button
            type="button"
            onClick={() => setView('history')}
            className={`px-2.5 py-1 rounded-md text-[11.5px] font-medium transition-colors whitespace-nowrap cursor-pointer ${
              view === 'history' ? 'bg-white/[0.12] text-white' : 'text-zinc-400 hover:text-zinc-200'
            }`}
          >
            History ({allowedNotifications.length})
          </button>
        </div>

        <div className="flex items-center gap-1">
          <button
            type="button"
            onClick={simulateIncomingNotification}
            className="flex items-center gap-1 px-2 py-1 rounded-md text-[11.5px] text-zinc-300 bg-white/[0.06] hover:bg-white/[0.12] hover:text-white transition-colors whitespace-nowrap cursor-pointer"
            title="Trigger test notification"
          >
            <Plus className="w-3 h-3" />
            <span>Test</span>
          </button>
          <button
            type="button"
            onClick={markAllNotificationsRead}
            className="p-1.5 rounded-md text-zinc-400 hover:text-white hover:bg-white/[0.08] transition-colors cursor-pointer"
            title="Mark all read"
            aria-label="Mark all notifications read"
          >
            <CheckCheck className="w-3.5 h-3.5" />
          </button>
          <button
            type="button"
            onClick={clearNotifications}
            className="px-2 py-1 rounded-md text-[11.5px] text-zinc-400 hover:text-zinc-100 hover:bg-white/[0.08] transition-colors whitespace-nowrap cursor-pointer"
          >
            Clear
          </button>
        </div>
      </div>

      {/* Notification List */}
      <div className="max-h-56 overflow-y-auto nova-scroll divide-y divide-white/[0.06]">
        {!settings.notifications.enabled ? (
          <div className="py-8 flex flex-col items-center justify-center text-zinc-500 gap-1.5">
            <BellOff className="w-5 h-5 opacity-60" />
            <span className="text-[12px]">Notification routing is paused in Settings.</span>
          </div>
        ) : visibleItems.length === 0 ? (
          <div className="py-8 flex flex-col items-center justify-center text-zinc-500 gap-1.5">
            <BellOff className="w-5 h-5 opacity-60" />
            <span className="text-[12px]">All caught up. No notifications.</span>
          </div>
        ) : (
          visibleItems.map((item) => (
            <div
              key={item.id}
              className={`py-2.5 px-1.5 flex items-start gap-3 hover:bg-white/[0.03] rounded-lg transition-colors ${
                item.dismissed ? 'opacity-55' : ''
              }`}
            >
              <AppBrandIcon appName={item.appName} />

              <div className="min-w-0 flex-1">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-1.5 text-[11px] text-zinc-400 min-w-0">
                    <span className="font-medium text-zinc-200">{item.appName}</span>
                    <span aria-hidden="true">·</span>
                    <span className="truncate">{item.timestamp}</span>
                    {!item.read && (
                      <>
                        <span aria-hidden="true">·</span>
                        <span style={{ color: 'var(--nova-accent)' }}>Unread</span>
                      </>
                    )}
                  </div>
                </div>
                <div className="text-[12.5px] font-medium text-white truncate mt-0.5">
                  {item.title}
                </div>
                <p className="text-[12px] text-zinc-400 line-clamp-2 mt-0.5 leading-relaxed">
                  {settings.notifications.showPreviews
                    ? item.body
                    : 'Message preview hidden by notification privacy setting.'}
                </p>
                {item.actionLabel && !item.dismissed && (
                  <button
                    type="button"
                    onClick={() => {
                      dismissNotification(item.id);
                      showToast(`${item.actionLabel} triggered for ${item.appName}`);
                    }}
                    className="mt-2 px-2.5 py-1 rounded-md text-[11px] font-medium text-zinc-200 bg-white/[0.07] hover:bg-white/[0.14] transition-colors cursor-pointer"
                  >
                    {item.actionLabel}
                  </button>
                )}
              </div>

              {!item.dismissed && (
                <button
                  type="button"
                  onClick={() => dismissNotification(item.id)}
                  className="p-1 rounded-md text-zinc-500 hover:text-zinc-200 hover:bg-white/[0.08] transition-colors cursor-pointer shrink-0"
                  aria-label="Dismiss notification"
                  title="Dismiss"
                >
                  <X className="w-3.5 h-3.5" />
                </button>
              )}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
