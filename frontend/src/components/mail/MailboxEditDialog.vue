<script setup lang="ts">
/**
 * Changes one mailbox's size limit, hourly send limit and on/off state, from the mailboxes list.
 *
 * A mailbox the panel suspended (its account or domain is) cannot be switched on from here: the
 * switch is drawn off and disabled, with the reason beside it, because the owner's choice and the
 * panel's are different facts and only the owner's is theirs to change. The client checks are the
 * ranges the panel states in its choices; the server is the authority and its already-localized
 * refusal is shown inside the dialog.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import { useMailboxesStore } from '../../stores/mailboxes'
import { readOptionalWholeNumber } from '../../utils/optionalWholeNumber'
import type { MailLimits } from '../../types/mailChoice'
import type { Mailbox } from '../../types/mailbox'

/** Props accepted by {@link MailboxEditDialog}; the page mounts it only while a mailbox is being edited. */
const props = defineProps<{
  /** The mailbox being changed. */
  mailbox: Mailbox
  /** The bounds the panel states for the form, or `null` while they have not arrived. */
  limits: MailLimits | null
}>()

/** Events emitted by {@link MailboxEditDialog}. */
const emit = defineEmits<{
  /** The dialog should close without a change. */
  (e: 'close'): void
  /** The panel accepted the change. */
  (e: 'saved'): void
}>()

const { t } = useI18n()
const store = useMailboxesStore()

/** The size limit as typed, in MiB; starts at the current one. */
const quota: Ref<string> = ref(String(props.mailbox.quotaMib))

/** The hourly send limit as typed; starts at the current one. */
const sendLimit: Ref<string> = ref(String(props.mailbox.sendLimitPerHour))

/** Whether the owner has the mailbox switched on: the stored choice, which a suspension does not change. */
const enabled: Ref<boolean> = ref(props.mailbox.enabled)

/** Whether a save has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** Whether the panel, not the owner, holds the mailbox off. */
const suspended: ComputedRef<boolean> = computed(() => {
  return props.mailbox.status === 'suspended'
})

/** Validation message for the size limit, or `null`. */
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

/** Validation message for the send limit, or `null`. */
const sendLimitError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const read = readOptionalWholeNumber(sendLimit.value)
  if (read.kind !== 'figure') {
    return t('mail.mailboxes.edit.errors.sendLimitInvalid')
  }
  const largest = props.limits?.maximumSendLimitPerHour
  return largest !== undefined && read.value > largest
    ? t('mail.mailboxes.edit.errors.sendLimitRange', { max: largest })
    : null
})

/**
 * Closes the dialog unless a request is in flight.
 * @returns Nothing.
 */
const close = (): void => {
  if (!store.acting) {
    emit('close')
  }
}

/**
 * Sends the change, and reports success to the page; a refusal stays on screen.
 * @returns Resolves once the request has settled.
 */
const submit = async (): Promise<void> => {
  submitted.value = true
  const quotaRead = readOptionalWholeNumber(quota.value)
  const sendRead = readOptionalWholeNumber(sendLimit.value)
  if (quotaError.value !== null || sendLimitError.value !== null || quotaRead.kind !== 'figure' || sendRead.kind !== 'figure') {
    return
  }
  const stored = await store.update(props.mailbox.id, {
    quotaMib: quotaRead.value,
    sendLimitPerHour: sendRead.value,
    enabled: enabled.value,
  })
  if (stored) {
    emit('saved')
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('mail.mailboxes.edit.title', { name: mailbox.address })"
    :close-label="t('common.close')"
    :dismissible="!store.acting"
    @close="close"
  >
    <UiSpinner v-if="store.acting" :label="t('mail.working')" />
    <UiForm v-else @submit="submit">
      <div class="flex flex-col gap-4">
        <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>
        <UiInput
          v-model="quota"
          mono
          inputmode="numeric"
          :label="t('mail.mailboxes.form.fields.quota')"
          :error="quotaError"
          required
        />
        <UiInput
          v-model="sendLimit"
          mono
          inputmode="numeric"
          :label="t('mail.mailboxes.edit.sendLimit')"
          :error="sendLimitError"
          required
        />
        <UiSwitch v-model="enabled" :label="t('mail.mailboxes.edit.enabled')" :disabled="suspended" />
        <p v-if="suspended" class="text-sm text-text-muted">{{ t('mail.mailboxes.edit.suspendedHint') }}</p>
      </div>
    </UiForm>

    <template #footer>
      <template v-if="!store.acting">
        <UiButton variant="secondary" @click="close">{{ t('common.cancel') }}</UiButton>
        <UiButton @click="submit">{{ t('mail.mailboxes.edit.submit') }}</UiButton>
      </template>
    </template>
  </UiModal>
</template>
