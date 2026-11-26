/**
 * NOVA Island — Domain Models & ViewModel Contracts
 * Structured cleanly to map 1:1 to C# WPF MVVM models and ObservableObjects.
 */

export type IslandMode =
  | 'compact'
  | 'music'
  | 'expanded'
  | 'lyrics'
  | 'clipboard'
  | 'notifications'
  | 'timer'
  | 'system'
  | 'activity';

export interface LyricLine {
  time: number; // seconds
  text: string;
  translation?: string;
}

export interface Track {
  id: string;
  title: string;
  artist: string;
  album: string;
  duration: number; // seconds
  artwork: string;
  source: 'Spotify' | 'Local Audio';
  lyricsSource: 'LRCLIB' | 'Spotify Sync' | 'Local .lrc';
  isSynced: boolean;
  bpm: number;
  lyrics: LyricLine[];
}

export type ClipboardItemType = 'text' | 'link' | 'code' | 'image';

export interface ClipboardItem {
  id: string;
  type: ClipboardItemType;
  content: string;
  previewTitle?: string;
  language?: string;
  imageUrl?: string;
  sourceApp: string;
  timestamp: string;
  pinned: boolean;
  sensitive?: boolean;
}

export interface NotificationItem {
  id: string;
  appName: 'Slack' | 'Outlook' | 'VS Code' | 'Discord' | 'GitHub' | 'Windows System';
  title: string;
  body: string;
  timestamp: string;
  read: boolean;
  dismissed?: boolean;
  actionLabel?: string;
}

export type ActivityType = 'download' | 'transfer' | 'screenshot';

export interface ActivityItem {
  id: string;
  type: ActivityType;
  title: string;
  subtitle: string;
  progress: number; // 0-100
  speed?: string;
  sizeText: string;
  status: 'active' | 'paused' | 'completed';
  imageUrl?: string;
  timestamp: string;
}

export type TimerTab = 'countdown' | 'stopwatch' | 'pomodoro';
export type PomodoroPhase = 'focus' | 'shortBreak' | 'longBreak';

export interface TimerState {
  activeTab: TimerTab;
  countdownDuration: number;
  countdownRemaining: number;
  countdownRunning: boolean;
  stopwatchElapsed: number;
  stopwatchRunning: boolean;
  stopwatchLaps: number[];
  pomodoroPhase: PomodoroPhase;
  pomodoroRemaining: number;
  pomodoroDuration: number;
  pomodoroRunning: boolean;
  pomodoroSessionsCompleted: number;
}

export interface AudioDevice {
  id: string;
  name: string;
  kind: 'speakers' | 'headphones' | 'interface';
  sampleRate: string;
}

export interface SystemMetrics {
  batteryLevel: number;
  isCharging: boolean;
  volume: number;
  isMuted: boolean;
  brightness: number;
  wifiConnected: boolean;
  wifiNetwork: string;
  wifiSignal: number; // 1-4
  micActive: boolean;
  micMuted: boolean;
  cameraActive: boolean;
  cpuUsage: number;
  ramUsage: number;
  ramUsedGb: number;
  ramTotalGb: number;
  activeAudioDeviceId: string;
  audioDevices: AudioDevice[];
}

export type ColorMode = 'dark' | 'light' | 'system';
export type ThemePreset = 'pure-black' | 'obsidian' | 'graphite' | 'contrast';
export type FontFamilyOption = 'Plus Jakarta Sans' | 'Manrope' | 'Outfit' | 'Segoe UI Variable';
export type AnimationPreset = 'fluid' | 'snappy' | 'silk' | 'instant';
export type ScreenPosition = 'top-left' | 'top-center' | 'top-right' | 'custom';
export type CollapseBehavior = 'outside-click' | 'mouse-leave' | 'manual';
export type DoubleClickAction =
  | 'toggle-lyrics'
  | 'open-fullscreen-lyrics'
  | 'open-dashboard'
  | 'toggle-play';

export interface ShortcutBinding {
  id: string;
  label: string;
  description: string;
  keys: string;
  action:
    | 'toggle-island'
    | 'open-music'
    | 'open-lyrics'
    | 'open-clipboard'
    | 'open-timer'
    | 'open-dashboard'
    | 'open-settings'
    | 'trigger-screenshot';
}

export interface WidgetVisibility {
  battery: boolean;
  volume: boolean;
  brightness: boolean;
  wifi: boolean;
  audioDevices: boolean;
  microphone: boolean;
  camera: boolean;
  cpu: boolean;
  ram: boolean;
  downloads: boolean;
  transfers: boolean;
  screenshots: boolean;
}

export interface PositionSettings {
  screenPosition: ScreenPosition;
  customX: number; // 0-100 percentage across monitor width
  customY: number; // 8-160 px from top edge
  monitor: string;
  rememberLastPosition: boolean;
}

export interface AppSettings {
  general: {
    appName: string;
    appShortName: string;
    launchOnStartup: boolean;
    alwaysOnTop: boolean;
    autoCollapseSeconds: number; // 0 = never
    collapseBehavior: CollapseBehavior;
    doubleClickAction: DoubleClickAction;
    islandEnabled: boolean;
    focusDndMode: boolean;
  };
  position: PositionSettings;
  appearance: {
    colorMode: ColorMode;
    theme: ThemePreset;
    accentColor: string;
    fontFamily: FontFamilyOption;
    fontSize: number; // 11 - 16
    fontWeight: 400 | 500 | 600;
    islandWidth: number; // compact width px 170 - 300
    islandHeight: number; // compact height px 32 - 48
    cornerRadius: number; // px 14 - 32
    opacity: number; // 80 - 100
    blur: number; // 0 - 32
    spacing: number; // 10 - 24
    animationSpeed: number; // 0.5 - 1.8 multiplier
  };
  animation: {
    preset: AnimationPreset;
    reduceMotion: boolean;
    hoverExpand: boolean;
    notificationBounce: boolean;
    smoothProgressRings: boolean;
    idleBreathing: boolean;
  };
  music: {
    showAlbumArtInCompact: boolean;
    showAudioVisualizer: boolean;
    showCompactProgress: boolean;
    autoExpandOnTrackChange: boolean;
    scrollLongTitles: boolean;
    interceptMediaKeys: boolean;
  };
  spotify: {
    connected: boolean;
    accountEmail: string;
    activeDevice: string;
    pollIntervalMs: 1000 | 2000 | 5000;
    syncArtworkAndQueue: boolean;
    preferHighResCovers: boolean;
  };
  lyrics: {
    preferSynchronized: boolean;
    lrclibFallback: boolean;
    sourcePriority:
      | 'Spotify -> LRCLIB -> Local'
      | 'LRCLIB -> Spotify -> Local'
      | 'Local -> LRCLIB';
    fontSize: number; // 13 - 22
    lineSpacing: number; // 1.3 - 2.0
    smoothScroll: boolean;
    blurInactiveLines: boolean;
    showTranslations: boolean;
    fullscreenBackdropOpacity: number; // 20 - 90
  };
  clipboard: {
    enabled: boolean;
    historyLimit: 25 | 50 | 100 | 250;
    autoClear: 'never' | '1h' | '24h' | 'restart';
    excludeSensitive: boolean;
    captureImages: boolean;
    persistPinned: boolean;
    excludedApps: string[];
  };
  notifications: {
    enabled: boolean;
    displayDuration: number; // seconds
    syncWindowsDnd: boolean;
    groupByApp: boolean;
    showPreviews: boolean;
    enabledApps: Record<NotificationItem['appName'], boolean>;
  };
  widgets: WidgetVisibility & {
    refreshIntervalMs: 1000 | 2000 | 5000;
  };
  shortcuts: ShortcutBinding[];
  privacy: {
    pauseInPrivateBrowsing: boolean;
    maskWhenLocked: boolean;
    diagnosticLogs: boolean;
    localOnlyMode: boolean;
  };
  storage: {
    albumCacheMb: number;
    lyricsCacheMb: number;
    clipboardImagesMb: number;
    configDbMb: number;
  };
}

export type SettingsSectionId =
  | 'general'
  | 'appearance'
  | 'position'
  | 'animation'
  | 'music'
  | 'spotify'
  | 'lyrics'
  | 'clipboard'
  | 'notifications'
  | 'widgets'
  | 'shortcuts'
  | 'privacy'
  | 'storage'
  | 'about';

export type DesktopWindowId = 'dashboard' | 'settings';
