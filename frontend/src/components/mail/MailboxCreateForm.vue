<script setup lang="ts">
/**
 * The create-a-mailbox form: the domain picker, the part of the address before the `@`, the
 * password the owner chooses, and the size limit. The owning account is never asked for — it is the
 * chosen domain's own — so an administrator picks a domain and a customer picks one of theirs.
 *
 * The client-side checks are the ones the panel states in its choices (the quota range and the
 * shortest password); everything else — whether the local part is a legal one, whether the address
 * is taken, whether the password is strong enough — is the server's to decide, and its
 * already-localized refusal is what the person reads (rules/vue.md "Forms").
 *
 * **It is its own panel.** The form draws a `UiCard` with a section heading. Dumb by the usual
 * contract: props in, emits out. It never touches a store or the API layer.
 */
import { computed, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiCombobox from '../ui/UiCombobox.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiPasswordInput from '../ui/UiPasswordInput.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import type { MailDomainPickerState } from '../../composables/useMailDomainChoice'
import { readOptionalWholeNumber } from '../../utils/optionalWholeNumber'
import type { MailLimits } from '../../types/mailChoice'
import type { CreateMailboxRequest } from '../../types/mailbox'

/** Props accepted by {@link MailboxCreateForm}. */
const props = defineProps<{
  /** The domain picker's state: the page of domains matching its search. */
  domainPicker: MailDomainPickerState
  /** The bounds the panel states for the form, or `null` while they have not arrived. */
  limits: MailLimits | null
  /** Whether a create request is already in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link MailboxCreateForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: CreateMailboxRequest): void
  /** Fired when the domain picker needs the domains matching what was typed into it. */
  (e: 'searchDomains', term: string): void
}>()

const { t } = useI18n()

/** The domain the mailbox lives in. */
const mailDomainId: Ref<string> = ref('')

/** The part of the address before the `@`. */
const localPart: Ref<string> = ref('')

/** The password the owner chose. */
const password: Ref<string> = ref('')

/** The size limit as typed, in MiB; starts at the panel's default once the bounds arrive. */
const quota: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the domain picker, or `null`. */
const domainError: ComputedRef<string | null> = computed(() => {
  return submitted.value && mailDomainId.value === '' ? t('mail.mailboxes.form.errors.domainRequired') : null
})

/** Validation message for the local part, or `null`. */
const localPartError: ComputedRef<string | null> = computed(() => {
  return submitted.value && localPart.value.trim() === '' ? t('mail.mailboxes.form.errors.localPartRequired') : null
})

/** Validation message for the password, or `null`. */
const passwordError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  if (password.value === '') {
    return t('mail.mailboxes.form.errors.passwordRequired')
  }
  const shortest = props.limits?.minimumPasswordLength ?? 0
  return password.value.length < shortest ? t('mail.mailboxes.form.errors.passwordShort', { n: shortest }) : null
})

/** Validation message for the quota, or `null`. */
const quotaError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const read = readOptionalWholeNumber(quota.value)
  if (read.kind !== 'figure') {
    return t('mail.mailboxes.form.errors.quotaInvalid')
  }
  const limits = props.limits
  if (limits !== null && (read.value < limits.minimumQuotaMib || read.value > limits.maximumQuotaMib)) {
    return t('mail.mailboxes.form.errors.quotaRange', { min: limits.minimumQuotaMib, max: limits.maximumQuotaMib })
  }
  return null
})

/** Whether every field currently passes the client's own rules. */
const isValid: ComputedRef<boolean> = computed(() => {
  return domainError.value === null && localPartError.value === null && passwordError.value === null && quotaError.value === null
})

/**
 * Validates, and emits only when the request is one the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  const read = readOptionalWholeNumber(quota.value)
  if (!isValid.value || read.kind !== 'figure') {
    return
  }
  emit('submit', {
    mailDomainId: mailDomainId.value,
    localPart: localPart.value.trim(),
    password: password.value,
    quotaMib: read.value,
  })
}

/**
 * Returns every field to its starting value and forgets that a submit was attempted, so the next
 * mailbox starts from a clean form. The password is cleared too: a form still holding the secret it
 * just sent invites the same submit twice.
 * @returns Nothing.
 */
const reset = (): void => {
  mailDomainId.value = ''
  localPart.value = ''
  password.value = ''
  quota.value = props.limits === null ? '' : String(props.limits.defaultQuotaMib)
  submitted.value = false
}

defineExpose({ reset })

// The default quota is the panel's, so it can only be filled once the bounds have arrived.
watch(
  (): MailLimits | null => {
    return props.limits
  },
  (limits: MailLimits | null): void => {
    if (limits !== null && quota.value === '') {
      quota.value = String(limits.defaultQuotaMib)
    }
  },
  { immediate: true },
)
</script>

<template>
  <UiCard>
    <UiSectionHeading class="mb-4" :title="t('mail.mailboxes.form.heading')" />
    <UiForm @submit="submit">
      <div class="grid gap-4 sm:grid-cols-2">
        <UiCombobox
          v-model="mailDomainId"
          v-bind="domainPicker"
          :label="t('mail.mailboxes.form.fields.domain')"
          :placeholder="t('mail.mailboxes.form.placeholders.domain')"
          :error="domainError"
          required
          @search="emit('searchDomains', $event)"
        />
        <UiInput
          v-model="localPart"
          mono
          :label="t('mail.mailboxes.form.fields.localPart')"
          :placeholder="t('mail.mailboxes.form.placeholders.localPart')"
          :error="localPartError"
          autocomplete="off"
          required
        />
        <UiPasswordInput
          v-model="password"
          :label="t('mail.mailboxes.form.fields.password')"
          :error="passwordError"
          autocomplete="new-password"
          generate
          required
        />
        <UiInput
          v-model="quota"
          mono
          inputmode="numeric"
          :label="t('mail.mailboxes.form.fields.quota')"
          :error="quotaError"
          required
        />
      </div>
      <div class="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <p class="min-w-0 text-sm text-text-muted">{{ t('mail.mailboxes.form.hint') }}</p>
        <UiButton class="shrink-0 self-start sm:self-auto" type="submit" :disabled="submitting">
          {{ t('mail.mailboxes.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
