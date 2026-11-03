import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { AccountFeatureName } from '../types/accountFeature'
import type { PlanFeatureChoice } from '../types/plan'
import type { ResellerEnvelope } from '../types/reseller'
import { usePlanCeilings, type PlanCeilings, type PlanCeilingKey } from './usePlanCeilings'
import { usePlanFeatures, type PlanFeatures } from './usePlanFeatures'

/** The count and disk figures an envelope states, by the name the request and the locale file both use. */
export type EnvelopeLimitKey = Exclude<keyof ResellerEnvelope, 'features' | PlanCeilingKey>

/** One envelope figure's bounds, as the server's validator states them. */
export interface EnvelopeLimitField {
  /** Which figure this is; also the suffix of its label's locale key. */
  key: EnvelopeLimitKey
  /** The smallest figure the server accepts. */
  min: number
  /** The largest figure the server accepts. */
  max: number
}

/** What {@link useResellerEnvelopeForm} hands the reseller form. */
export interface ResellerEnvelopeForm {
  /** The figures in the order the form draws them. */
  fields: readonly EnvelopeLimitField[]
  /** Each figure as typed text. */
  values: Ref<Record<EnvelopeLimitKey, string>>
  /** The runtime ceilings: their state, validation and request shape. */
  runtime: PlanCeilings
  /** The features the reseller may include in a plan: what is ticked, and whether the set can be sent. */
  features: PlanFeatures
  /** Whether every figure, every ceiling and the feature set pass the client's mirror of the server's rules. */
  isValid: ComputedRef<boolean>
  /** The message to show for one figure, or `null`. */
  errorOf: (field: EnvelopeLimitField) => string | null
  /** The envelope as the request states it; only meaningful once `isValid` holds. */
  toRequest: () => ResellerEnvelope
}

/**
 * The envelope's figures in the order the form draws them. The lower bound is 1 where "none" would
 * make the reseller unable to sell anything (accounts, plans, disk) and 0 where "none" is a product
 * (FTPS logins, cron entries). The upper bounds are typo and overflow guards, far above what a
 * server can honour; the server's validator is the authority and these err wide.
 */
const FIELDS: readonly EnvelopeLimitField[] = [
  { key: 'maxAccounts', min: 1, max: 1_000_000 },
  { key: 'maxPlans', min: 1, max: 1_000_000 },
  { key: 'diskPoolMb', min: 1, max: 1_073_741_824 },
  { key: 'maxDiskQuotaMb', min: 1, max: 1_073_741_824 },
  { key: 'maxSites', min: 1, max: 1_000_000 },
  { key: 'maxDatabases', min: 1, max: 1_000_000 },
  { key: 'maxSftpUsers', min: 1, max: 1_000_000 },
  { key: 'maxFtpUsers', min: 0, max: 1_000_000 },
  { key: 'maxCronEntries', min: 0, max: 1_000_000 },
  { key: 'maxPhpWorkersPerPool', min: 1, max: 10_000 },
]

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
 * The reseller form's envelope: ten figures typed as text, three runtime ceilings (each a figure
 * or a ticked "No limit", through `usePlanCeilings`, because an envelope states them exactly as a
 * plan does) and the features the reseller may sell (through `usePlanFeatures`). The rules are the
 * client's mirror of the server's validator and are advice; the server re-validates and its
 * refusal is what the page shows.
 *
 * Nothing starts at a made-up default: a new envelope starts empty rather than at a figure the SPA
 * invented, because a default is a domain value and domain values come from the backend.
 * @param envelope The envelope being edited, or `null` for a new reseller's.
 * @param submitted Whether a submit has been attempted, so nothing turns red before that.
 * @param choices The panel's features, read when needed; `null` while unknown.
 * @returns The figures, ceilings and features, their validation and the request shape.
 */
export const useResellerEnvelopeForm = (
  envelope: ResellerEnvelope | null,
  submitted: Readonly<Ref<boolean>>,
  choices: () => PlanFeatureChoice[] | null,
): ResellerEnvelopeForm => {
  const { t } = useI18n()

  const values: Ref<Record<EnvelopeLimitKey, string>> = ref(
    Object.fromEntries(
      FIELDS.map((field): [EnvelopeLimitKey, string] => {
        return [field.key, envelope === null ? '' : String(envelope[field.key])]
      }),
    ) as Record<EnvelopeLimitKey, string>,
  )

  const runtime = usePlanCeilings(envelope, submitted)

  const features = usePlanFeatures(envelope, choices)

  const isValid: ComputedRef<boolean> = computed(() => {
    return (
      FIELDS.every((field) => {
        return errorOf(field) === null
      }) &&
      runtime.isValid.value &&
      features.isReady.value
    )
  })

  /**
   * Validates one figure against its bounds.
   * @param field The figure to check.
   * @returns The message to show, or `null` when acceptable (or nothing was tried yet).
   */
  const errorOf = (field: EnvelopeLimitField): string | null => {
    if (!submitted.value) {
      return null
    }
    const figure = parseWhole(values.value[field.key])
    if (figure === null) {
      return t('resellers.form.errors.whole')
    }
    return figure < field.min || figure > field.max
      ? t('resellers.form.errors.range', { min: field.min, max: field.max })
      : null
  }

  /**
   * Reads one figure for the request. Called only once `isValid` holds, so the fallback of zero
   * merely satisfies the type and is never sent.
   * @param key Which figure to read.
   * @returns The figure.
   */
  const figureOf = (key: EnvelopeLimitKey): number => {
    return parseWhole(values.value[key]) ?? 0
  }

  /**
   * States the envelope as the request carries it.
   * @returns The envelope, every field present.
   */
  const toRequest = (): ResellerEnvelope => {
    const selected: AccountFeatureName[] = features.selected.value
    return {
      maxAccounts: figureOf('maxAccounts'),
      diskPoolMb: figureOf('diskPoolMb'),
      maxPlans: figureOf('maxPlans'),
      maxDiskQuotaMb: figureOf('maxDiskQuotaMb'),
      maxSites: figureOf('maxSites'),
      maxDatabases: figureOf('maxDatabases'),
      maxSftpUsers: figureOf('maxSftpUsers'),
      maxFtpUsers: figureOf('maxFtpUsers'),
      maxCronEntries: figureOf('maxCronEntries'),
      maxPhpWorkersPerPool: figureOf('maxPhpWorkersPerPool'),
      ...runtime.toRequest(),
      features: selected,
    }
  }

  return { fields: FIELDS, values, runtime, features, isValid, errorOf, toRequest }
}
