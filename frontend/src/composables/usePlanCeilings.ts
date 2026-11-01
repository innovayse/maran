import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ManagedPlan, PlanCeiling, PlanRequest } from '../types/plan'

/** The runtime ceilings a plan states, by the name the request and the locale file both use. */
export type PlanCeilingKey = 'cpuQuotaPercent' | 'memoryMaxMb' | 'maxProcesses'

/** One runtime ceiling's upper bound, as the server's validator (and the agent behind it) states it. */
export interface PlanCeilingField {
  /** Which ceiling this is; also the suffix of its label's locale key. */
  key: PlanCeilingKey
  /** The largest figure the server accepts. */
  max: number
}

/** One runtime ceiling as the form holds it: "No limit" ticked, or a typed figure. */
export interface PlanCeilingState {
  /** Whether "No limit" is ticked; the figure is then neither checked nor sent. */
  unlimited: boolean
  /** The figure as typed. */
  figure: string
}

/** What {@link usePlanCeilings} hands the plan form. */
export interface PlanCeilings {
  /** The ceilings in the order the form draws them. */
  fields: readonly PlanCeilingField[]
  /** Each ceiling as the form holds it. */
  ceilings: Ref<Record<PlanCeilingKey, PlanCeilingState>>
  /** Whether every ceiling passes the client's mirror of the server's rules. */
  isValid: ComputedRef<boolean>
  /** The message to show for one ceiling, or `null`. */
  errorOf: (field: PlanCeilingField) => string | null
  /** The three ceilings as the request states them; only meaningful once `isValid` holds. */
  toRequest: () => Pick<PlanRequest, PlanCeilingKey>
}

/**
 * The runtime ceilings in the order the form draws them, with `PlanDefinitionValidator`'s upper
 * bounds: 1024 whole CPUs, a pebibyte of memory, and the kernel's own process-id ceiling.
 */
const FIELDS: readonly PlanCeilingField[] = [
  { key: 'cpuQuotaPercent', max: 102_400 },
  { key: 'memoryMaxMb', max: 1_073_741_824 },
  { key: 'maxProcesses', max: 4_194_304 },
]

/**
 * How the form starts one ceiling: from the plan's own, or with neither a figure nor "No limit" for
 * a new plan, so the administrator decides rather than inheriting a default the SPA made up.
 * @param ceiling The plan's ceiling, or `undefined` for a new plan.
 * @returns The ceiling as the form holds it.
 */
const startOf = (ceiling: PlanCeiling | undefined): PlanCeilingState => {
  if (ceiling === undefined) {
    return { unlimited: false, figure: '' }
  }
  return ceiling.kind === 'unlimited'
    ? { unlimited: true, figure: '' }
    : { unlimited: false, figure: String(ceiling.maximum) }
}

/**
 * Reads typed text as a whole number.
 * @param text What the administrator typed.
 * @returns The number, or `null` when the text is not digits only.
 */
const parseWhole = (text: string): number | null => {
  const trimmed = text.trim()
  return /^\d+$/.test(trimmed) ? Number(trimmed) : null
}

/**
 * The plan form's runtime ceilings (issue #133): CPU, memory and processes, each a figure or a
 * ticked "No limit", never a zero standing for "none" — the server refuses both a limited zero and
 * an unlimited ceiling carrying a figure. The rules mirror `ResourceCeilingValidator` and are advice;
 * the server re-validates, and its refusal is what the page shows.
 * @param plan The plan being edited, or `null` for a new one.
 * @param submitted Whether a submit has been attempted, so nothing turns red before that.
 * @returns The ceilings' fields, state, validation and request shape.
 */
export const usePlanCeilings = (plan: ManagedPlan | null, submitted: Readonly<Ref<boolean>>): PlanCeilings => {
  const { t } = useI18n()

  const ceilings: Ref<Record<PlanCeilingKey, PlanCeilingState>> = ref({
    cpuQuotaPercent: startOf(plan?.cpuQuotaPercent),
    memoryMaxMb: startOf(plan?.memoryMaxMb),
    maxProcesses: startOf(plan?.maxProcesses),
  })

  const isValid: ComputedRef<boolean> = computed(() => {
    return FIELDS.every((field) => {
      return errorOf(field) === null
    })
  })

  /**
   * Validates one ceiling: nothing to check when "No limit" is ticked, otherwise a whole number from
   * 1 to its bound.
   * @param field The ceiling to check.
   * @returns The message to show, or `null` when acceptable (or nothing was tried yet).
   */
  const errorOf = (field: PlanCeilingField): string | null => {
    const state = ceilings.value[field.key]
    if (!submitted.value || state.unlimited) {
      return null
    }
    const figure = parseWhole(state.figure)
    if (figure === null) {
      return t('plans.form.errors.whole')
    }
    return figure < 1 || figure > field.max ? t('plans.form.errors.range', { min: 1, max: field.max }) : null
  }

  /**
   * Reads one ceiling for the request. Called only once `isValid` holds, so the fallback of zero
   * merely satisfies the type and is never sent.
   * @param key Which ceiling to read.
   * @returns The ceiling, its case stated.
   */
  const ceilingOf = (key: PlanCeilingKey): PlanCeiling => {
    const state = ceilings.value[key]
    return state.unlimited
      ? { kind: 'unlimited', maximum: 0 }
      : { kind: 'limited', maximum: parseWhole(state.figure) ?? 0 }
  }

  /**
   * States the three ceilings as the request carries them.
   * @returns The request's ceiling fields.
   */
  const toRequest = (): Pick<PlanRequest, PlanCeilingKey> => {
    return {
      cpuQuotaPercent: ceilingOf('cpuQuotaPercent'),
      memoryMaxMb: ceilingOf('memoryMaxMb'),
      maxProcesses: ceilingOf('maxProcesses'),
    }
  }

  return { fields: FIELDS, ceilings, isValid, errorOf, toRequest }
}
