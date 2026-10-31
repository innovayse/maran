<script setup lang="ts">
/**
 * The create-an-FTPS-login form: the account picker, the login name, and the client-side mirror of
 * the server's validator (`CreateFtpUserCommandValidator`).
 *
 * **A disabled FTPS daemon is a normal state, not an error.** The daemon ships installed, stopped
 * and bound to nothing until an administrator turns it on, so the form stays drawn with its submit
 * unavailable and a sentence saying why. It is not hidden (a missing form reads as a panel that has
 * no FTPS at all) and it shows no alert (nothing has gone wrong).
 *
 * **When the panel did not tell this caller whether FTPS is on, the form stays open.** The status
 * endpoint is administrator-only, so a customer cannot learn the daemon's state; blocking the form
 * on an unknown would take FTPS away from every customer on a working server. The server is the
 * authority either way and its already-localized refusal is what the operator reads — a client-side
 * check errs permissive on purpose (rules/security.md: enforcement is the backend's).
 *
 * **It is its own panel.** The form draws a `UiCard` with a section heading, so the page composes it
 * like any other panel of the screen and passes no wrapper; the accent submit pill and its hint sit
 * in one row under the fields and stack below 640px so neither is squeezed into a one-word column.
 *
 * Dumb by the usual contract: props in, emits out. It never touches a store or the API layer, and it
 * refuses to emit until its own rules pass.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import type { FtpsAvailability } from '../../types/ftpsStatus'
import type { CreateFtpUserRequest } from '../../types/ftpUser'

/**
 * The server's suffix alphabet, character for character: lowercase ASCII letters and digits, and
 * nothing else. Narrower than a legal Unix user name on purpose — the value becomes a `useradd`
 * argument and a path segment — and it excludes the underscore separator, so account `alice` cannot
 * ask for `bob_deploy` and be handed a login that reads as `bob`'s in `/etc/passwd`.
 *
 * This is advice that saves a round trip. The server re-validates it, and its already-localized
 * rejection is what the operator reads when the two disagree.
 */
const SUFFIX_PATTERN = /^[a-z0-9]+$/

/** The longest suffix the server accepts before the account prefix is applied. */
const MAX_SUFFIX_LENGTH = 30

/** Props accepted by {@link FtpUserCreateForm}. */
const props = defineProps<{
  /**
   * The account picker's state — the page of accounts a login may be created for matching the last
   * search — read by the page from its store; the form only renders it and asks for searches.
   */
  accountPicker: AccountPickerState
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
  /** What the panel has told this caller about the FTPS daemon. */
  ftpsAvailability: FtpsAvailability
  /**
   * The account every request is for when the caller has no choice (a customer's own), or `null`
   * for an administrator. When set, the picker is not drawn and the account rule is skipped.
   */
  fixedAccountId: string | null
}>()

/** Events emitted by {@link FtpUserCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateFtpUserRequest): void
  /** Fired when the account picker needs the accounts matching what was typed into it. */
  (e: 'searchAccounts', term: string): void
}>()

const { t } = useI18n()

/** The account that will own the new login. */
const accountId: Ref<string> = ref('')

/** The login name the customer chose, without the account prefix. */
const name: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the operator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Whether the panel has said, in so many words, that the FTPS daemon is turned off. */
const isFtpsOff: ComputedRef<boolean> = computed(() => {
  return props.ftpsAvailability === 'off'
})

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || props.fixedAccountId !== null) {
    return null
  }
  return accountId.value.length === 0 ? t('ftp.form.errors.accountRequired') : null
})

/** Validation message for the login name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (name.value.length === 0) {
    return t('ftp.form.errors.nameRequired')
  }
  return name.value.length > MAX_SUFFIX_LENGTH || !SUFFIX_PATTERN.test(name.value)
    ? t('ftp.form.errors.nameInvalid')
    : null
})

/** Whether every field currently passes the client's own mirror of the server's rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return accountError.value === null && nameError.value === null
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (!isValid.value || isFtpsOff.value) {
    return
  }
  emit('submit', { accountId: props.fixedAccountId ?? accountId.value, name: name.value })
}

/**
 * Returns every field to its starting value and forgets that a submit was attempted, so the next
 * login starts from a clean form. The account is cleared too: a form still showing the values it
 * just sent invites the same submit twice (#218).
 * @returns Nothing.
 */
const reset = (): void => {
  accountId.value = ''
  name.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('ftp.form.heading')" />
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiCombobox
          v-if="fixedAccountId === null"
          v-model="accountId"
          v-bind="accountPicker"
          :label="t('ftp.form.fields.account')"
          :placeholder="t('ftp.form.placeholders.account')"
          :error="accountError"
          required
          @search="emit('searchAccounts', $event)"
        />
        <UiInput
          v-model="name"
          :label="t('ftp.form.fields.name')"
          :placeholder="t('ftp.form.placeholders.name')"
          :error="nameError"
          required
        />
      </div>
      <!-- Stated as a fact about the server, not as a failure: the daemon ships installed and
           stopped, and an administrator turning it on is a deliberate act with firewall
           consequences. -->
      <p v-if="isFtpsOff" data-testid="ftps-off-note" class="mt-4 text-sm text-text-muted">
        {{ t('ftp.form.ftpsOffNote') }}
      </p>
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('ftp.form.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting || isFtpsOff">
          {{ t('ftp.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
