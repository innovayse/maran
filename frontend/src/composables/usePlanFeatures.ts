import { computed, ref, type ComputedRef, type Ref } from 'vue'
import type { ManagedPlan, PlanFeatureChoice } from '../types/plan'

/** What {@link usePlanFeatures} hands the plan form. */
export interface PlanFeatures {
  /** The features currently ticked, by name, in the order the panel lists them. */
  selected: ComputedRef<string[]>
  /**
   * Whether the set can be sent as it stands. An edit always can: it starts from the plan's own set
   * and sends it back unchanged if the panel's list never arrived. A new plan can only once the
   * panel's list is known, because "every feature" is the panel's list and the SPA holds none.
   */
  isReady: ComputedRef<boolean>
  /** Ticks or clears one feature. */
  toggle: (feature: string, ticked: boolean) => void
}

/**
 * The feature set of the plan form: which of the panel's features are ticked.
 *
 * **Nothing is ticked by the SPA's own choice.** Until the administrator touches a box the set is
 * what the plan already has, and for a new plan every feature the panel lists — the same default
 * the server applies to a plan created without a list. Only the first toggle materialises a set of
 * the form's own, so a list that arrives late still decides the starting point of an untouched form.
 * @param plan The plan being edited, or `null` for a new one.
 * @param choices The panel's features, read when needed; `null` while unknown.
 * @returns The ticked set and what changes it.
 */
export const usePlanFeatures = (
  plan: ManagedPlan | null,
  choices: () => PlanFeatureChoice[] | null,
): PlanFeatures => {
  /** The administrator's own set once they have toggled a box; `null` while the form is untouched. */
  const touched: Ref<string[] | null> = ref(null)

  /** The panel's feature names in the panel's order. */
  const offered: ComputedRef<string[]> = computed(() => {
    return (choices() ?? []).map((choice) => {
      return choice.feature
    })
  })

  /** The ticked set: the administrator's own once they toggled, else the plan's, else every offered feature. */
  const selected: ComputedRef<string[]> = computed(() => {
    return touched.value ?? plan?.features ?? offered.value
  })

  /** Whether the set can be sent: always for an edit, for a new plan once the panel's list is known. */
  const isReady: ComputedRef<boolean> = computed(() => {
    return plan !== null || choices() !== null
  })

  /**
   * Ticks or clears one feature, keeping the panel's order so the request reads the same however
   * the boxes were clicked.
   * @param feature The feature's name on the wire.
   * @param ticked Whether the box is now ticked.
   * @returns Nothing.
   */
  const toggle = (feature: string, ticked: boolean): void => {
    const others = selected.value.filter((name) => {
      return name !== feature
    })
    const next = ticked ? [...others, feature] : others
    // A feature the panel no longer lists (a plan edited after a feature was removed) keeps its
    // place at the end rather than being dropped by a toggle of something else.
    touched.value = [
      ...offered.value.filter((name) => {
        return next.includes(name)
      }),
      ...next.filter((name) => {
        return !offered.value.includes(name)
      }),
    ]
  }

  return { selected, isReady, toggle }
}
