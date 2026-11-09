import type { StoredSettingApi } from './loading'

/**
 * One entry of the dashboard layout, mirroring the backend's layout entry: a key from the panel's
 * closed vocabulary and whether this person has hidden it. The key is a name the SPA maps to a
 * component or a shortcut; a key this SPA has no mapping for (a newer panel's) is skipped, never an
 * error.
 */
export interface DashboardLayoutEntry {
  /** The widget or shortcut, by the panel's key. */
  key: string
  /** True when this person turned it off. A hidden entry stays in the list so it can be turned back on in its place. */
  hidden: boolean
}

/**
 * The EFFECTIVE layout for the signed-in person, mirroring the backend's `DashboardLayoutDto`: the
 * panel's defaults for their audience and for the modules that are available, with their own
 * choices applied. Both lists are in the order they are drawn.
 */
export interface DashboardLayout {
  /** The widgets, in the order the dashboard draws them. */
  widgets: DashboardLayoutEntry[]
  /** The shortcuts of the quick-actions bar, in the order it draws them. */
  actions: DashboardLayoutEntry[]
}

/** What `PUT /api/v1/me/dashboard-layout` takes: the whole layout, which replaces the stored one. */
export type SaveDashboardLayoutRequest = DashboardLayout

/** The dashboard-layout endpoint this SPA calls: a singleton setting read whole and replaced whole. */
export type DashboardLayoutApi = StoredSettingApi<DashboardLayout, SaveDashboardLayoutRequest>
