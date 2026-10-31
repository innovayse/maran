<script setup lang="ts">
/**
 * The create-an-SFTP-login form: the account picker, the login name, and the client-side mirror of
 * the server's validator (`CreateSftpUserCommandValidator`).
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
import type { CreateSftpUserRequest } from '../../types/sftpUser'

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

/** Props accepted by {@link SftpUserCreateForm}. */
const props = defineProps<{
  /**
   * The account picker's state — the page of accounts a login may be created for matching the last
   * search — read by the page from its store; the form only renders it and asks for searches.
   */
  accountPicker: AccountPickerState
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
  /**
   * The account every request is for when the caller has no choice (a customer's own), or `null`
   * for an administrator. When set, the picker is not drawn and the account rule is skipped.
   */
  fixedAccountId: string | null
}>()

/** Events emitted by {@link SftpUserCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateSftpUserRequest): void
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

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || props.fixedAccountId !== null) {
    return null
  }
  return accountId.value.length === 0 ? t('sftp.form.errors.accountRequired') : null
})

/** Validation message for the login name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (name.value.length === 0) {
    return t('sftp.form.errors.nameRequired')
  }
  return name.value.length > MAX_SUFFIX_LENGTH || !SUFFIX_PATTERN.test(name.value)
    ? t('sftp.form.errors.nameInvalid')
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
  if (!isValid.value) {
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
    <UiSectionHeading class="mb-4" :title="t('sftp.form.heading')" />
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiCombobox
          v-if="fixedAccountId === null"
          v-model="accountId"
          v-bind="accountPicker"
          :label="t('sftp.form.fields.account')"
          :placeholder="t('sftp.form.placeholders.account')"
          :error="accountError"
          required
          @search="emit('searchAccounts', $event)"
        />
        <UiInput
          v-model="name"
          :label="t('sftp.form.fields.name')"
          :placeholder="t('sftp.form.placeholders.name')"
          :error="nameError"
          required
        />
      </div>
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('sftp.form.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting">
          {{ t('sftp.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
