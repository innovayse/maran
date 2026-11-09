import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { QUICK_ACTIONS } from '../router/quickActions'
import { useDashboardLayoutStore } from '../stores/dashboardLayout'
import { useModulesStore } from '../stores/modules'
import type { QuickAction } from '../types/quickAction'

/** One widget the dashboard can draw. */
export interface DashboardWidgetSpec {
  /** The panel's key for it; the layout names widgets by this. */
  key: string
  /** Whether it takes the full width of the grid rather than half. */
  wide: boolean
}

/**
 * Every widget this bundle can draw, in the order the dashboard falls back to when the panel's
 * layout cannot be read. The panel's own default order (and a person's choices) replace this order;
 * what stays here is the set the bundle knows how to draw and how wide each is.
 */
export const DASHBOARD_WIDGETS: readonly DashboardWidgetSpec[] = [
  { key: 'attention', wide: true },
  { key: 'firstRun', wide: true },
  { key: 'cpu', wide: false },
  { key: 'memory', wide: false },
  { key: 'disk', wide: false },
  { key: 'network', wide: false },
  { key: 'services', wide: true },
  { key: 'totals', wide: true },
  { key: 'activity', wide: true },
]

/** What {@link useDashboardWidgets} gives the dashboard page. */
export interface DashboardWidgets {
  /** The widgets to draw, in the person's order, without the ones they hid. */
  widgets: ComputedRef<DashboardWidgetSpec[]>
  /** The shortcuts to draw, in the person's order, without hidden ones and without ones the licence does not permit. */
  actions: ComputedRef<QuickAction[]>
  /** The keys of every widget this bundle can draw, for the customise sheet. */
  knownWidgetKeys: readonly string[]
}

/**
 * Resolves the dashboard's widgets and shortcuts from the person's layout.
 *
 * The panel holds the layout and answers the effective one; this composable only maps its keys to
 * what the bundle can draw. A key the bundle does not know is skipped. While the layout is not
 * known (not read yet, or unreadable) every widget the bundle can draw is shown in its own order, so
 * a failed preference read never blanks the dashboard.
 * @returns The widgets, the shortcuts and the known widget keys.
 */
export const useDashboardWidgets = (): DashboardWidgets => {
  const { t } = useI18n()
  const layoutStore = useDashboardLayoutStore()
  const modules = useModulesStore()

  /** The widgets to draw. */
  const widgets: ComputedRef<DashboardWidgetSpec[]> = computed(() => {
    const layout = layoutStore.layout
    if (layout === null) {
      return [...DASHBOARD_WIDGETS]
    }
    return layout.widgets.flatMap((entry) => {
      const spec = DASHBOARD_WIDGETS.find((candidate) => {
        return candidate.key === entry.key
      })
      return spec === undefined || entry.hidden ? [] : [spec]
    })
  })

  /** The shortcuts to draw. */
  const actions: ComputedRef<QuickAction[]> = computed(() => {
    const layout = layoutStore.layout
    const wanted =
      layout === null
        ? QUICK_ACTIONS
        : layout.actions.flatMap((entry) => {
            const spec = QUICK_ACTIONS.find((candidate) => {
              return candidate.key === entry.key
            })
            return spec === undefined || entry.hidden ? [] : [spec]
          })
    return wanted
      .filter((spec) => {
        return spec.module === null || modules.isEnabled(spec.module)
      })
      .map((spec): QuickAction => {
        // The catalogue row minus its key: the translated label replaces `labelKey`.
        const { labelKey, ...shortcut } = spec
        return { ...shortcut, label: t(labelKey) }
      })
  })

  return {
    widgets,
    actions,
    knownWidgetKeys: DASHBOARD_WIDGETS.map((widget) => {
      return widget.key
    }),
  }
}
