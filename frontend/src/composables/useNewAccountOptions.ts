import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { CreateAccountRequest, OwnerLoginMode, Plan } from '../types/account'
import { readOptionalWholeNumber, type OptionalWholeNumber } from '../utils/optionalWholeNumber'

/** The largest inode figure the server accepts: the quota tool takes 32-bit figures (`MaxInodeLimit`). */
const MAX_INODE_LIMIT = 4294967295

/** The largest process figure the server accepts: the kernel's `PID_MAX_LIMIT` (`MaxProcessLimit`). */
const MAX_PROCESS_LIMIT = 4194304

/** The largest open-files figure the agent accepts (`OpenFilesLimit`). */
const MAX_OPEN_FILES_LIMIT = 1048576

/** The part of {@link CreateAccountRequest} the owner-login and limit fields decide. */
export type NewAccountOptionsRequest = Pick<
  CreateAccountRequest,
  'loginMode' | 'initialPassword' | 'inodeLimit' | 'processLimit' | 'openFilesLimit'
>

/** What {@link useNewAccountOptions} hands the create-account form. */
export interface NewAccountOptions {
  /** How the owner's login is made. */
  loginMode: Ref<OwnerLoginMode>
  /** The owner's first password as typed; empty unless the mode asks for one. */
  initialPassword: Ref<string>
  /** The inode limit as typed; empty follows the plan. */
  inodeLimit: Ref<string>
  /** The process limit as typed; empty follows the plan. */
  processLimit: Ref<string>
  /** The open-files limit as typed; empty follows the plan. */
  openFilesLimit: Ref<string>
  /** Message under the password field, or `null`. */
  passwordError: ComputedRef<string | null>
  /** Message under the inode-limit field, or `null`. */
  inodeLimitError: ComputedRef<string | null>
  /** Message under the process-limit field, or `null`. */
  processLimitError: ComputedRef<string | null>
  /** Message under the open-files field, or `null`. */
  openFilesLimitError: ComputedRef<string | null>
  /** Whether every owner-login and limit field passes the client-side check. */
  valid: ComputedRef<boolean>
  /**
   * The request members these fields decide. Members left at their default are left out, so a
   * form that was not touched sends the body it always sent.
   * @returns The members to spread into the create request.
   */
  toRequest: () => NewAccountOptionsRequest
  /** Forgets the typed password; called once it was sent and when the form goes away. */
  clearPassword: () => void
}

/**
 * State and checks of the create-account form's owner-login choice and per-account limits
 * (issue #194), kept out of the page so it stays a layout of fields.
 *
 * The checks are advice (rules/security.md item 6): they catch the slip before a round trip, and
 * the server decides. In particular a reseller's "only lower than the plan" rule is checked here
 * only against a plan that reports a process ceiling; every other case errs permissive and the
 * server refuses in its own words.
 * @param submitted Whether a submit was attempted; messages show only after it.
 * @param plan The plan chosen so far, `undefined` while none is.
 * @param isReseller Whether the caller is a reseller, who may only lower a limit below the plan.
 * @returns The fields, their messages and the request fragment.
 */
export const useNewAccountOptions = (
  submitted: Ref<boolean>,
  plan: () => Plan | undefined,
  isReseller: () => boolean,
): NewAccountOptions => {
  const { t } = useI18n()

  /** How the owner's login is made; the invitation is the default. */
  const loginMode: Ref<OwnerLoginMode> = ref('invitation')
  /** The owner's first password as typed. */
  const initialPassword: Ref<string> = ref('')
  /** The inode limit as typed. */
  const inodeLimit: Ref<string> = ref('')
  /** The process limit as typed. */
  const processLimit: Ref<string> = ref('')
  /** The open-files limit as typed. */
  const openFilesLimit: Ref<string> = ref('')

  /** The password field's message: a password is required only in the initial-password mode. */
  const passwordError = computed((): string | null => {
    if (!submitted.value || loginMode.value !== 'initialPassword') {
      return null
    }
    return initialPassword.value.length === 0 ? t('accounts.form.errors.passwordRequired') : null
  })

  /** The inode field's message. */
  const inodeLimitError = computed((): string | null => {
    return submitted.value ? boundsError(readOptionalWholeNumber(inodeLimit.value), 0, MAX_INODE_LIMIT) : null
  })

  /** The process field's message, including a reseller's lower-only rule. */
  const processLimitError = computed((): string | null => {
    if (!submitted.value) {
      return null
    }
    const read = readOptionalWholeNumber(processLimit.value)
    const bounds = boundsError(read, 1, MAX_PROCESS_LIMIT)
    if (bounds !== null) {
      return bounds
    }
    const ceiling = plan()?.maxProcesses
    if (isReseller() && read.kind === 'figure' && ceiling?.kind === 'limited' && read.value > ceiling.maximum) {
      return t('accounts.form.errors.limitAbovePlan', { maximum: ceiling.maximum })
    }
    return null
  })

  /** The open-files field's message. */
  const openFilesLimitError = computed((): string | null => {
    return submitted.value ? boundsError(readOptionalWholeNumber(openFilesLimit.value), 0, MAX_OPEN_FILES_LIMIT) : null
  })

  /** Whether every field passes. */
  const valid = computed((): boolean => {
    return (
      passwordError.value === null &&
      inodeLimitError.value === null &&
      processLimitError.value === null &&
      openFilesLimitError.value === null
    )
  })

  /**
   * Checks one limit field against the bounds the server's validator states.
   * @param read What the field holds.
   * @param smallest The smallest accepted figure (`0` means "no limit" for inodes and open files).
   * @param largest The largest accepted figure.
   * @returns The message to show, or `null`.
   */
  const boundsError = (read: OptionalWholeNumber, smallest: number, largest: number): string | null => {
    if (read.kind === 'invalid') {
      return t('accounts.form.errors.limitInvalid')
    }
    if (read.kind === 'figure' && read.value < smallest) {
      return t('accounts.form.errors.limitTooSmall', { minimum: smallest })
    }
    if (read.kind === 'figure' && read.value > largest) {
      return t('accounts.form.errors.limitTooLarge', { maximum: largest })
    }
    return null
  }

  /**
   * Turns a field's text into a request figure.
   * @param text The field's text.
   * @returns The figure, or `undefined` when the field is empty (the plan decides).
   */
  const figureOf = (text: string): number | undefined => {
    const read = readOptionalWholeNumber(text)
    return read.kind === 'figure' ? read.value : undefined
  }

  /**
   * Builds the request fragment, leaving defaults out.
   * @returns The members to spread into the create request.
   */
  const toRequest = (): NewAccountOptionsRequest => {
    const inode = figureOf(inodeLimit.value)
    const processes = figureOf(processLimit.value)
    const openFiles = figureOf(openFilesLimit.value)
    return {
      ...(loginMode.value === 'initialPassword'
        ? { loginMode: loginMode.value, initialPassword: initialPassword.value }
        : {}),
      ...(inode === undefined ? {} : { inodeLimit: inode }),
      ...(processes === undefined ? {} : { processLimit: processes }),
      ...(openFiles === undefined ? {} : { openFilesLimit: openFiles }),
    }
  }

  /** Forgets the typed password. */
  const clearPassword = (): void => {
    initialPassword.value = ''
  }

  return {
    loginMode,
    initialPassword,
    inodeLimit,
    processLimit,
    openFilesLimit,
    passwordError,
    inodeLimitError,
    processLimitError,
    openFilesLimitError,
    valid,
    toRequest,
    clearPassword,
  }
}
