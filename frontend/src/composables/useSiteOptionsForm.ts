import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useSiteOptionsStore } from '../stores/siteOptions'
import type {
  BasicAuthRow,
  CanonicalHostWire,
  IpAccessModeWire,
  ReplaceSiteOptionsRequest,
  SiteOptions,
  SiteOptionsSection,
  SiteRedirect,
} from '../types/siteOptions'

/**
 * Which section each family of refusal codes is shown under. Used to place a message only: the
 * words are always the server's own.
 */
const SECTION_PREFIXES: readonly (readonly [string, SiteOptionsSection])[] = [
  ['SiteCanonicalHost', 'canonicalHost'],
  ['SiteBodySize', 'uploadSize'],
  ['SiteCacheDays', 'cache'],
  ['SiteRedirect', 'redirects'],
  ['SiteBasicAuth', 'basicAuth'],
  ['SiteIpRule', 'ipRules'],
  ['SiteIpAccessMode', 'ipRules'],
  ['SiteRequestRate', 'requestRate'],
  ['SiteRequestBurst', 'requestBurst'],
]

/** A number field the form itself refused to send, and the local note that says why. */
interface NumberProblem {
  /** The section the note is shown under. */
  section: SiteOptionsSection
  /** The locale key of the note. */
  key: string
  /** Figures the note names, when it names any. */
  params?: Record<string, number>
}

/** What {@link useSiteOptionsForm} hands to the options screen. */
export interface SiteOptionsForm {
  /** The canonical host as its select binds it. */
  canonicalHost: Ref<string>
  /** The upload size as typed, in MiB; empty leaves the web server's default. */
  bodySize: Ref<string>
  /** The cache lifetime as typed, in days; empty sends no cache headers. */
  cacheDays: Ref<string>
  /** The redirect rules being edited. */
  redirects: Ref<SiteRedirect[]>
  /** The basic-authentication users being edited. */
  users: Ref<BasicAuthRow[]>
  /** The IP access mode as its select binds it. */
  ipMode: Ref<string>
  /** The IP rules as typed, one per line. */
  ipText: Ref<string>
  /** The request rate as typed, in requests per second; empty leaves the site unlimited. */
  rateText: Ref<string>
  /** The request burst as typed; empty means no burst. */
  burstText: Ref<string>
  /** The options on screen, once the panel has answered. */
  options: ComputedRef<SiteOptions | null>
  /** The server's message, when the refusal is not about one of the sections. */
  generalError: ComputedRef<string | null>
  /** Whether turning protection on would be the first user the site has. */
  turnsAuthOn: ComputedRef<boolean>
  /** The server's message under one section, or `null` when the last refusal belongs elsewhere. */
  errorFor: (section: SiteOptionsSection) => string | null
  /** The text under a number field: the local note for a non-number, otherwise the server's message. */
  numberErrorFor: (section: SiteOptionsSection) => string | null
  /** Builds the request the form states, or `null` when a number field is not a whole number. */
  buildRequest: () => ReplaceSiteOptionsRequest | null
}

/**
 * The state of the site options form: what is typed in each section, how it is filled from the options
 * the panel holds, how it becomes a request, and where a refusal is shown.
 *
 * The form is refilled whenever the panel hands over a set: on the first read and after a successful
 * save (which restates the stored set, with every password field emptied again). A refused save does
 * not change `options`, so what was typed stays.
 *
 * @returns The form's state and the functions that read and write it.
 */
export const useSiteOptionsForm = (): SiteOptionsForm => {
  const { t } = useI18n()
  const store = useSiteOptionsStore()

  const canonicalHost: Ref<string> = ref('')
  const bodySize: Ref<string> = ref('')
  const cacheDays: Ref<string> = ref('')
  const redirects: Ref<SiteRedirect[]> = ref([])
  const users: Ref<BasicAuthRow[]> = ref([])
  const ipMode: Ref<string> = ref('')
  const ipText: Ref<string> = ref('')
  const rateText: Ref<string> = ref('')
  const burstText: Ref<string> = ref('')

  /** The number field the form refused to send, kept apart from the server's message. */
  const numberProblem: Ref<NumberProblem | null> = ref(null)

  const options: ComputedRef<SiteOptions | null> = computed(() => {
    return store.options
  })

  /** The section the last refusal belongs to, or `null` when it names none (or there was none). */
  const refusedSection: ComputedRef<SiteOptionsSection | null> = computed(() => {
    const code = store.errorCode
    if (code === null) {
      return null
    }
    // Longest prefix first, so "SiteIpAccessMode" is not read as the shorter family it also starts.
    const match = [...SECTION_PREFIXES]
      .sort((left, right) => {
        return right[0].length - left[0].length
      })
      .find(([prefix]) => {
        return code.startsWith(prefix)
      })
    return match === undefined ? null : match[1]
  })

  const generalError: ComputedRef<string | null> = computed(() => {
    return refusedSection.value === null ? store.errorMessage : null
  })

  const turnsAuthOn: ComputedRef<boolean> = computed(() => {
    return (options.value?.basicAuthUsers.length ?? 0) === 0 && users.value.length > 0
  })

  /**
   * The server's message under one section, or `null` when the last refusal belongs elsewhere.
   * @param section The section asking.
   * @returns The message to show under it.
   */
  const errorFor = (section: SiteOptionsSection): string | null => {
    return refusedSection.value === section ? store.errorMessage : null
  }

  /**
   * The text a number field shows under it: the local note for a value that is not a whole number,
   * otherwise the server's message.
   * @param section The number field's section.
   * @returns The message, or `null`.
   */
  const numberErrorFor = (section: SiteOptionsSection): string | null => {
    return numberProblem.value?.section === section ? t(numberProblem.value.key, numberProblem.value.params ?? {}) : errorFor(section)
  }

  /**
   * Fills the form from the options the panel holds.
   * @param held The options to show.
   * @returns Nothing; the form state is replaced.
   */
  const fill = (held: SiteOptions): void => {
    canonicalHost.value = held.canonicalHost
    bodySize.value = held.clientMaxBodySizeMib === null ? '' : String(held.clientMaxBodySizeMib)
    cacheDays.value = held.staticCacheDays === null ? '' : String(held.staticCacheDays)
    redirects.value = held.redirects.map((rule) => {
      return { ...rule }
    })
    users.value = held.basicAuthUsers.map((name) => {
      return { name, password: '', existing: true }
    })
    // The mode select always shows a value the panel offers; with no rules it is the first choice and
    // is not sent (see `buildRequest`), so it carries no meaning of its own.
    ipMode.value = held.ipAccessMode ?? held.choices.ipAccessModes[0]?.value ?? ''
    ipText.value = held.ipRules.join('\n')
    rateText.value = held.requestRate === null ? '' : String(held.requestRate.perSecond)
    burstText.value = held.requestRate === null ? '' : String(held.requestRate.burst)
    numberProblem.value = null
  }

  /**
   * Reads a number field: empty means "not set", digits mean a number, anything else is a problem.
   * @param text What was typed.
   * @returns The number, `null` for empty, or `undefined` when the text is not a whole number.
   */
  const readNumber = (text: string): number | null | undefined => {
    const trimmed = text.trim()
    if (trimmed.length === 0) {
      return null
    }
    return /^\d+$/.test(trimmed) ? Number.parseInt(trimmed, 10) : undefined
  }

  /**
   * Builds the request the form states, or `null` when a number field is not a whole number.
   * @returns The complete new set.
   */
  const buildRequest = (): ReplaceSiteOptionsRequest | null => {
    const size = readNumber(bodySize.value)
    const days = readNumber(cacheDays.value)
    const rate = readNumber(rateText.value)
    const burst = readNumber(burstText.value)
    // Each check narrows its own value, so everything below holds plain numbers or `null`.
    if (size === undefined) {
      numberProblem.value = { section: 'uploadSize', key: 'siteOptions.errors.notANumber' }
      return null
    }
    // The plan's upload bound is advice here: the panel refuses a larger figure on its own and is the
    // enforcement, so this only saves the round trip. Absent bound, nothing to check. Only a figure that
    // CHANGED is checked, as the panel does: the form echoes the stored figure back with every save, and one
    // a later plan edit left above the bound must not block saving the other options.
    const planMaximum = options.value?.limits.planMaximumBodySizeMib ?? null
    const stored = options.value?.clientMaxBodySizeMib ?? null
    if (size !== null && size !== stored && planMaximum !== null && size > planMaximum) {
      numberProblem.value = {
        section: 'uploadSize',
        key: 'siteOptions.errors.uploadAbovePlan',
        params: { maximum: planMaximum },
      }
      return null
    }
    if (days === undefined) {
      numberProblem.value = { section: 'cache', key: 'siteOptions.errors.notANumber' }
      return null
    }
    if (rate === undefined) {
      numberProblem.value = { section: 'requestRate', key: 'siteOptions.errors.notANumber' }
      return null
    }
    if (burst === undefined) {
      numberProblem.value = { section: 'requestBurst', key: 'siteOptions.errors.notANumber' }
      return null
    }
    // A burst only has meaning on top of a rate, and the panel would drop it with no word: say so here
    // instead of letting the operator believe a burst was saved.
    if (rate === null && burst !== null) {
      numberProblem.value = { section: 'requestRate', key: 'siteOptions.errors.burstNeedsRate' }
      return null
    }
    numberProblem.value = null

    const rules = ipText.value
      .split('\n')
      .map((line) => {
        return line.trim()
      })
      .filter((line) => {
        return line.length > 0
      })

    return {
      // The select only offers values the panel listed, so the cast restates what it already holds.
      canonicalHost: canonicalHost.value as CanonicalHostWire,
      clientMaxBodySizeMib: size,
      staticCacheDays: days,
      redirects: redirects.value,
      basicAuthUsers: users.value.map((user) => {
        return { name: user.name.trim(), password: user.password.length > 0 ? user.password : null }
      }),
      // A mode with no addresses means nothing, so none is sent with an empty list.
      ipAccessMode: rules.length > 0 ? (ipMode.value as IpAccessModeWire) : null,
      ipRules: rules,
      // An empty rate leaves the site unlimited; an empty burst beside a rate means no burst.
      requestRate: rate === null ? null : { perSecond: rate, burst: burst ?? 0 },
    }
  }

  watch(
    options,
    (held) => {
      if (held !== null) {
        fill(held)
      }
    },
    { immediate: true },
  )

  return {
    canonicalHost,
    bodySize,
    cacheDays,
    redirects,
    users,
    ipMode,
    ipText,
    rateText,
    burstText,
    options,
    generalError,
    turnsAuthOn,
    errorFor,
    numberErrorFor,
    buildRequest,
  }
}
