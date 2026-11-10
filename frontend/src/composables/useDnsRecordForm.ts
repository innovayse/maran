import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { DnsRecordCatalog, DnsRecordField, DnsRecordTypeInfo } from '../types/dns'
import type { DnsRecordRequest } from '../types/dnsRecord'

/** The server's longest owner name, in characters. */
const MAX_NAME_LENGTH = 253

/** The apex, as a record's owner name is written for it. */
const APEX_NAME = '@'

/**
 * An owner name relative to the zone: `@`, or dot-separated labels of letters, digits, `-` and `_`
 * (service labels such as `_dmarc`) that neither start nor end with `-`, with `*` allowed only as
 * the whole leftmost label. The server lower-cases the name, so capitals are accepted here too.
 */
const OWNER_NAME_PATTERN = /^(?:\*|(?!-)[A-Za-z0-9_-]{1,63}(?<!-))(?:\.(?!-)[A-Za-z0-9_-]{1,63}(?<!-))*$/

/** A non-negative whole number written in digits. */
const DIGITS_PATTERN = /^\d+$/

/** The most a 16-bit field (priority, weight, port) holds. */
const MAX_UINT16 = 65_535

/** The most a one-byte field (CAA flags) holds. */
const MAX_UINT8 = 255

/** The message for each input of the record form, or `null` while it is fine or untouched. */
export type DnsRecordFormErrors = Record<'name' | 'type' | 'ttl' | 'value' | DnsRecordField, string | null>

/** The form state and validation of one DNS record, shared by the record dialog. */
export interface DnsRecordForm {
  /** The owner name relative to the zone. */
  name: Ref<string>
  /** The chosen record type. */
  type: Ref<string>
  /** The TTL as typed. */
  ttl: Ref<string>
  /** The value as typed. */
  value: Ref<string>
  /** The priority as typed. */
  priority: Ref<string>
  /** The weight as typed. */
  weight: Ref<string>
  /** The port as typed. */
  port: Ref<string>
  /** The flags as typed. */
  flags: Ref<string>
  /** The CAA tag as chosen. */
  tag: Ref<string>
  /** Whether a submit has been attempted, so nothing turns red before the person has tried. */
  submitted: Ref<boolean>
  /** The chosen type's catalogue entry, or `null` while it is unknown. */
  typeInfo: ComputedRef<DnsRecordTypeInfo | null>
  /** The extra inputs the chosen type takes. */
  fields: ComputedRef<DnsRecordField[]>
  /** The message for each input, or `null` while it is fine or untouched. */
  errors: ComputedRef<DnsRecordFormErrors>
  /** Whether every input currently passes the mirror of the server's rules. */
  isValid: ComputedRef<boolean>
  /**
   * Fills the form from a record, or empties it for a new one.
   * @param record The record to edit, or `null` for a new one.
   * @returns Nothing.
   */
  reset: (record: DnsRecordFormSource | null) => void
  /**
   * Builds the request the panel takes from the inputs.
   * @returns The record, with a number or a tag only for the inputs the type takes; `null` while
   * the chosen type is not one the panel's catalogue lists.
   */
  toRequest: () => DnsRecordRequest | null
}

/**
 * What the form fills from: a record, or a template's record, whose TTL may be absent and then
 * follows the name server settings.
 */
export type DnsRecordFormSource = Omit<DnsRecordRequest, 'ttl'> & { ttl: number | null }

/** What a caller can ask of the form beyond the panel's catalogue. */
export interface DnsRecordFormOptions {
  /**
   * Whether the TTL may be left empty. A zone's record always has one; a template's record may
   * leave it out, and the zone then takes the default TTL of the name server settings.
   */
  ttlOptional?: boolean
}

/** The messages of a form nothing was submitted from: every input is fine until a submit proves otherwise. */
const NO_ERRORS: DnsRecordFormErrors = {
  name: null,
  type: null,
  ttl: null,
  value: null,
  priority: null,
  weight: null,
  port: null,
  flags: null,
  tag: null,
}

/**
 * Whether a text carries a control character, a newline above all: the server refuses one in any
 * value (rules/security.md item 4), so the form says so before asking.
 * @param text The text to look at.
 * @returns True when any character is a control character.
 */
const hasControlCharacter = (text: string): boolean => {
  return [...text].some((character): boolean => {
    const code = character.charCodeAt(0)
    return code < 0x20 || code === 0x7f
  })
}

/**
 * Reads a whole number from its text.
 * @param text The text the person typed.
 * @returns The number, or `NaN` when it is not digits.
 */
const toNumber = (text: string): number => {
  return DIGITS_PATTERN.test(text.trim()) ? Number(text.trim()) : Number.NaN
}

/**
 * Builds the form state and the client-side mirror of the server's record rules. Which record
 * types exist, which extra inputs each takes, the TTL and length bounds and the CAA tags all come
 * from the panel's catalogue; nothing about a record type is decided here. What a value must look
 * like per type is left to the server, which judges an address, a target and a text each by its
 * own rule and answers in the reader's language; the form checks only what holds for every type.
 * The mirror is advice that saves a round trip: the server validates everything again and its
 * localized refusal is shown as it was sent.
 * @param catalog The reference data, or `null` while it has not arrived (the form stays permissive).
 * @param options Whether the TTL may be left empty.
 * @returns The form state and its validation.
 */
export const useDnsRecordForm = (
  catalog: Ref<DnsRecordCatalog | null>,
  options: DnsRecordFormOptions = {},
): DnsRecordForm => {
  const { t } = useI18n()

  /** The owner name relative to the zone, as typed. */
  const name: Ref<string> = ref('')
  /** The chosen record type on the wire. */
  const type: Ref<string> = ref('')
  /** The TTL in seconds, as typed. */
  const ttl: Ref<string> = ref('')
  /** The value, as typed. */
  const value: Ref<string> = ref('')
  /** The priority, as typed. */
  const priority: Ref<string> = ref('')
  /** The weight, as typed. */
  const weight: Ref<string> = ref('')
  /** The port, as typed. */
  const port: Ref<string> = ref('')
  /** The flags, as typed. */
  const flags: Ref<string> = ref('')
  /** The chosen CAA tag. */
  const tag: Ref<string> = ref('')
  /** Whether a submit has been attempted. */
  const submitted: Ref<boolean> = ref(false)

  // The functions below are declared after the computed state that reads them; a computed runs
  // only when first read, by which point every function exists.

  /** The catalogue entry of the chosen type. */
  const typeInfo: ComputedRef<DnsRecordTypeInfo | null> = computed(() => {
    return (
      catalog.value?.types.find((candidate): boolean => {
        return candidate.type === type.value
      }) ?? null
    )
  })

  /** The extra inputs the chosen type takes, in the order the form draws them. */
  const fields: ComputedRef<DnsRecordField[]> = computed(() => {
    const info = typeInfo.value
    if (info === null) {
      return []
    }
    const taken: DnsRecordField[] = []
    if (info.usesPriority) {
      taken.push('priority')
    }
    if (info.usesWeightAndPort) {
      taken.push('weight', 'port')
    }
    if (info.usesFlagsAndTag) {
      taken.push('flags', 'tag')
    }
    return taken
  })

  /** Every input's message, whether or not a submit was attempted. */
  const allErrors: ComputedRef<DnsRecordFormErrors> = computed(() => {
    return {
      name: nameError(),
      type: typeInfo.value === null ? t('dns.record.errors.typeRequired') : null,
      ttl: ttlError(),
      value: valueError(),
      priority: numberError('priority', priority.value, MAX_UINT16),
      weight: numberError('weight', weight.value, MAX_UINT16),
      port: numberError('port', port.value, MAX_UINT16),
      flags: numberError('flags', flags.value, MAX_UINT8),
      tag: tagError(),
    }
  })

  /** The messages shown: none before the first submit, so the dialog opens clean. */
  const errors: ComputedRef<DnsRecordFormErrors> = computed(() => {
    return submitted.value ? allErrors.value : NO_ERRORS
  })

  /** Whether every input passes. */
  const isValid: ComputedRef<boolean> = computed(() => {
    return Object.values(allErrors.value).every((message): boolean => {
      return message === null
    })
  })

  /**
   * Checks the owner name.
   * @returns The message, or `null` when it passes.
   */
  const nameError = (): string | null => {
    const text = name.value.trim()
    if (text.length === 0) {
      return t('dns.record.errors.nameRequired')
    }
    if (text !== APEX_NAME && (text.length > MAX_NAME_LENGTH || !OWNER_NAME_PATTERN.test(text))) {
      return t('dns.record.errors.nameInvalid')
    }
    return null
  }

  /**
   * Checks the TTL against the panel's bounds.
   * @returns The message, or `null` when it passes.
   */
  const ttlError = (): string | null => {
    if (options.ttlOptional === true && ttl.value.trim() === '') {
      return null
    }
    const seconds = toNumber(ttl.value)
    if (Number.isNaN(seconds)) {
      return t('dns.record.errors.ttlInvalid')
    }
    const bounds = catalog.value
    if (bounds !== null && (seconds < bounds.minTtl || seconds > bounds.maxTtl)) {
      return t('dns.record.errors.ttlRange', { min: bounds.minTtl, max: bounds.maxTtl })
    }
    return null
  }

  /**
   * Checks what holds for a value of every type: it is there, within the length the panel states,
   * and carries no control character, a newline above all (rules/security.md item 4). The shape of
   * an address, a host name or a text is the server's to judge, type by type.
   * @returns The message, or `null` when it passes.
   */
  const valueError = (): string | null => {
    const text = value.value.trim()
    if (text.length === 0) {
      return t('dns.record.errors.valueRequired')
    }
    const longest = catalog.value?.maxValueLength
    if (longest !== undefined && text.length > longest) {
      return t('dns.record.errors.valueTooLong', { max: longest })
    }
    return hasControlCharacter(text) ? t('dns.record.errors.valueControl') : null
  }

  /**
   * Checks one numeric input.
   * @param field The input.
   * @param text What was typed.
   * @param limit The largest value the field holds.
   * @returns The message, or `null` when the type does not take the input or it passes.
   */
  const numberError = (field: DnsRecordField, text: string, limit: number): string | null => {
    if (!fields.value.includes(field)) {
      return null
    }
    const number = toNumber(text)
    return Number.isNaN(number) || number > limit ? t('dns.record.errors.numberRange', { max: limit }) : null
  }

  /**
   * Checks the CAA tag against the panel's allow-list.
   * @returns The message, or `null` when the type takes no tag or it passes.
   */
  const tagError = (): string | null => {
    if (!fields.value.includes('tag')) {
      return null
    }
    const allowed = catalog.value?.caaTags
    const listed = allowed === undefined || allowed.includes(tag.value)
    return tag.value.length === 0 || !listed ? t('dns.record.errors.tagInvalid') : null
  }

  /**
   * Shows a stored number as the text a field edits. A field the record's type does not take is
   * stored as zero and shown empty, so it does not read as a value the person chose.
   * @param stored The number the record holds, or `undefined` for a new record.
   * @param taken Whether the record's type takes the field.
   * @returns Its digits, or the empty string.
   */
  const asText = (stored: number | undefined, taken: boolean): string => {
    return stored === undefined || !taken ? '' : String(stored)
  }

  /**
   * Fills the form from a record, or empties it for a new one.
   * @param record The record to edit, or `null` for a new one.
   * @returns Nothing.
   */
  const reset = (record: DnsRecordFormSource | null): void => {
    submitted.value = false
    name.value = record?.name ?? ''
    type.value = record?.type ?? catalog.value?.types[0]?.type ?? ''
    // An optional TTL starts empty, since empty is its own meaning (the settings' default).
    const startingTtl = options.ttlOptional === true ? '' : String(catalog.value?.defaultTtl ?? '')
    ttl.value = record === null || record.ttl === null ? startingTtl : String(record.ttl)
    value.value = record?.value ?? ''
    // `fields` reads `type`, which was just set, so each input is shown only when the type takes it.
    priority.value = asText(record?.priority, fields.value.includes('priority'))
    weight.value = asText(record?.weight, fields.value.includes('weight'))
    port.value = asText(record?.port, fields.value.includes('port'))
    flags.value = asText(record?.flags, fields.value.includes('flags'))
    tag.value = record?.tag ?? ''
  }

  /**
   * Builds the request the panel takes from the inputs. An input the type does not take is sent
   * as zero (or the empty tag): the panel refuses a value there. The type is taken from the
   * catalogue's own entry for the chosen one, so what is sent is a type the panel named.
   * @returns The record, or `null` while the chosen type is not one the catalogue lists.
   */
  const toRequest = (): DnsRecordRequest | null => {
    const info = typeInfo.value
    if (info === null) {
      return null
    }
    return {
      name: name.value.trim(),
      type: info.type,
      ttl: toNumber(ttl.value),
      value: value.value.trim(),
      priority: fields.value.includes('priority') ? toNumber(priority.value) : 0,
      weight: fields.value.includes('weight') ? toNumber(weight.value) : 0,
      port: fields.value.includes('port') ? toNumber(port.value) : 0,
      flags: fields.value.includes('flags') ? toNumber(flags.value) : 0,
      tag: fields.value.includes('tag') ? tag.value : '',
    }
  }

  return {
    name,
    type,
    ttl,
    value,
    priority,
    weight,
    port,
    flags,
    tag,
    submitted,
    typeInfo,
    fields,
    errors,
    isValid,
    reset,
    toRequest,
  }
}
