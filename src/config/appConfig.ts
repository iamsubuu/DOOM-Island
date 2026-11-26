/**
 * NOVA Island — Centralized Application Identity & Architecture Config
 * Change DEFAULT_APP_NAME / DEFAULT_APP_SHORT_NAME here to rebrand the entire UI
 * without modifying individual components. Maps to AppConstants.cs in C# WPF.
 */

export const APP_CONFIG = {
  defaultAppName: 'NOVA Island',
  defaultShortName: 'NOVA',
  version: '1.1.0-prototype',
  targetPlatform: 'Windows 11 · C# WPF (Antigravity)',
  storageKey: 'nova_island_settings_v2',
  positionStorageKey: 'nova_island_last_position_v2',
} as const;
