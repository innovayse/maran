<script setup lang="ts">
/**
 * The add-a-mail-domain form: the account picker (an administrator's), the domain name, and the
 * client-side check that the name is not empty. Whether the name is a hostname of one of the
 * account's own sites, and whether another account already holds it, is the server's to decide, and
 * its already-localized refusal is what the person reads.
 *
 * **It is its own panel.** The form draws a `UiCard` with a section heading, so the page composes it
 * like any other panel of the screen. Dumb by the usual contract: props in, emits out. It never
 * touches a store or the API layer, and it refuses to emit until its own rule passes.
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
import type { CreateMailDomainRequest } from '../../types/mailDomain'

/** Props accepted by {@link MailDomainCreateForm}. */
const props = defineProps<{
  /** The account picker's state: the page of accounts a domain may be added for. */
  accountPicker: AccountPickerState
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
  /**
   * The account every request is for when the caller has no choice (a customer's own), or `null`
   * for an administrator. When set, the picker is not drawn and the account rule is skipped.
   */
  fixedAccountId: string | null
}>()

/** Events emitted by {@link MailDomainCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateMailDomainRequest): void
  /** Fired when the account picker needs the accounts matching what was typed into it. */
  (e: 'searchAccounts', term: string): void
}>()

const { t } = useI18n()

/** The account that will own the new domain. */
const accountId: Ref<string> = ref('')

/** The domain name the person typed. */
const name: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the account picker, or `null`. */
const accountError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value || props.fixedAccountId !== null) {
    return null
  }
  return accountId.value.length === 0 ? t('mail.domains.form.errors.accountRequired') : null
})

/** Validation message for the domain name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  return name.value.trim().length === 0 ? t('mail.domains.form.errors.nameRequired') : null
})

/** Whether every field currently passes the client's own rules. */
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
  emit('submit', { accountId: props.fixedAccountId ?? accountId.value, name: name.value.trim() })
}

/**
 * Returns every field to its starting value and forgets that a submit was attempted, so the next
 * domain starts from a clean form.
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
    <UiSectionHeading class="mb-4" :title="t('mail.domains.form.heading')" />
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiCombobox
          v-if="fixedAccountId === null"
          v-model="accountId"
          v-bind="accountPicker"
          :label="t('mail.domains.form.fields.account')"
          :placeholder="t('mail.domains.form.placeholders.account')"
          :error="accountError"
          required
          @search="emit('searchAccounts', $event)"
        />
        <UiInput
          v-model="name"
          mono
          :label="t('mail.domains.form.fields.name')"
          :placeholder="t('mail.domains.form.placeholders.name')"
          :error="nameError"
          required
        />
      </div>
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('mail.domains.form.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting">
          {{ t('mail.domains.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
