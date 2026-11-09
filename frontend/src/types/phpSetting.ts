/**
 * How a PHP setting's value is read and edited, as the panel names it. The panel decides the kind
 * of each setting; the SPA only draws the control that fits it.
 *
 * - `size`: a whole number of mebibytes, written as digits (`"256"`); the SPA never builds or parses
 *   the `M` suffix of php.ini.
 * - `seconds`: a whole number of seconds.
 * - `count`: a whole number.
 * - `timezone`: the name of one of the zones the panel offers in {@link PhpSettingItem.choices}.
 */
export type PhpSettingKind = 'size' | 'seconds' | 'count' | 'timezone'

/**
 * One PHP setting a customer may change for their account, mirroring the backend's
 * `PhpSettingItemDto` field-for-field. The catalogue travels with the answer, so the SPA holds no
 * setting names, bounds or units of its own (rules/vue.md: "Data comes from the backend").
 */
export interface PhpSettingItem {
  /** The setting's name on the wire (`memory_limit`, ...); the key of a stored value. */
  name: string
  /** The setting's label, already in the request's language. */
  label: string
  /** How the value is read and edited. */
  kind: PhpSettingKind
  /** The unit word for the value (`MiB`, `seconds`), already in the request's language; `null` for a count and a timezone. */
  unit: string | null
  /** What the customer stored, in the wire spelling of its kind; `null` when it was never set. */
  value: string | null
  /** What the pool carries after the panel clamped the stored value to the plan; `null` when PHP's own default applies. */
  effectiveValue: string | null
  /** The largest value the plan allows, in the item's unit; `null` for a timezone. */
  planMaximum: number | null
  /** Whether the stored value is above {@link planMaximum}, so the plan's maximum is what runs. */
  exceedsPlan: boolean
  /** For a timezone only: the IANA zone names the server knows; `null` for every other kind. */
  choices: string[] | null
}

/**
 * The customer's PHP settings as `GET` and `PUT /api/v1/php-settings` answer, mirroring the
 * backend's `PhpSettingsDto`.
 */
export interface PhpSettingsDto {
  /** The account the settings belong to: always the signed-in customer's own. */
  accountId: string
  /** Every setting the customer may change, set or not. */
  items: PhpSettingItem[]
  /** How many of the account's PHP sites the settings reach (their pool carries them). */
  sitesAffected: number
}

/** One stored setting in a save request. */
export interface PhpSettingValue {
  /** The setting's name, as the item named it. */
  name: string
  /** The value in the wire spelling of the item's kind. */
  value: string
}

/**
 * Request body for `PUT /api/v1/php-settings`, binding the backend's command. A FULL replacement:
 * a setting that is not listed is removed from the account, which puts it back to PHP's default.
 * The account is never in the body; the server takes it from the signed-in customer.
 */
export interface SavePhpSettingsRequest {
  /** Every setting the account keeps. */
  settings: PhpSettingValue[]
}

/**
 * Typed access to the customer's PHP settings.
 *
 * Called from Pinia stores only, never from a component (rules/vue.md).
 */
export interface PhpSettingsApi {
  /**
   * Reads the account's PHP settings with the catalogue and the plan's bounds.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings.
   */
  get: (signal?: AbortSignal) => Promise<PhpSettingsDto>

  /**
   * Replaces the account's stored settings and applies them to its PHP pools.
   * @param request The settings to keep; any other is removed.
   * @param signal Optional abort signal to cancel the in-flight request.
   * @returns The settings as they now stand.
   */
  save: (request: SavePhpSettingsRequest, signal?: AbortSignal) => Promise<PhpSettingsDto>
}
