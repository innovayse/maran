<script setup lang="ts">
/**
 * The add-a-key form of the keys panel: a label and one pasted public-key line, with the client-side
 * mirror of the server's rules. Dumb by the usual contract: props in, emits out. It refuses to emit
 * until its own rules pass, but only the server decides whether a key is acceptable — its message,
 * shown by the panel, wins on any disagreement.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiTextarea from '../ui/UiTextarea.vue'
import { sftpPublicKeyProblem, type SftpPublicKeyProblem } from '../../utils/sftpPublicKeyProblem'
import type { AddSftpUserKeyRequest } from '../../types/sftpUser'

/** The longest label the server accepts. */
const MAX_LABEL_LENGTH = 64

/** Props accepted by {@link SftpKeyAddForm}. */
defineProps<{
  /** Whether an add request is in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link SftpKeyAddForm}. */
const emit = defineEmits<{
  /** Fired only when every client-side rule passes, carrying the request to send. */
  (e: 'submit', request: AddSftpUserKeyRequest): void
}>()

const { t } = useI18n()

/** The name the person gives the key. */
const label: Ref<string> = ref('')

/** The pasted key text, as typed. */
const publicKey: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/**
 * The message of each key problem, by explicit key: every locale key stays greppable and visible to
 * the locale-parity check, which a key assembled from the problem's name would not be.
 */
const keyProblemMessages: ComputedRef<Record<SftpPublicKeyProblem, string>> = computed(() => {
  return {
    empty: t('sftp.keys.form.errors.key.empty'),
    multiline: t('sftp.keys.form.errors.key.multiline'),
    tooLong: t('sftp.keys.form.errors.key.tooLong'),
    algorithm: t('sftp.keys.form.errors.key.algorithm'),
    format: t('sftp.keys.form.errors.key.format'),
  }
})

/** Validation message for the label, or `null`. */
const labelError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const length = label.value.trim().length
  if (length === 0) {
    return t('sftp.keys.form.errors.labelRequired')
  }
  return length > MAX_LABEL_LENGTH ? t('sftp.keys.form.errors.labelTooLong') : null
})

/** Validation message for the key text, or `null`. */
const keyError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const problem = sftpPublicKeyProblem(publicKey.value)
  return problem === null ? null : keyProblemMessages.value[problem]
})

/**
 * Validates, and emits only a request the server has a chance of accepting.
 * @returns Nothing; emits synchronously when the form is valid.
 */
const submit = (): void => {
  submitted.value = true
  if (labelError.value !== null || keyError.value !== null) {
    return
  }
  emit('submit', { label: label.value.trim(), publicKey: publicKey.value.trim() })
}

/**
 * Empties both fields and forgets that a submit was attempted, so the next key starts clean.
 * @returns Nothing.
 */
const reset = (): void => {
  label.value = ''
  publicKey.value = ''
  submitted.value = false
}

defineExpose({ reset })
</script>

<template>
  <UiForm class="flex flex-col gap-3" @submit="submit">
    <UiInput
      v-model="label"
      :label="t('sftp.keys.form.fields.label')"
      :placeholder="t('sftp.keys.form.placeholders.label')"
      :error="labelError"
      required
    />
    <UiTextarea
      v-model="publicKey"
      :label="t('sftp.keys.form.fields.key')"
      :placeholder="t('sftp.keys.form.placeholders.key')"
      :rows="4"
      :error="keyError"
      required
    />
    <p class="text-sm text-text-muted">{{ t('sftp.keys.form.hint') }}</p>
    <UiButton class="self-start" type="submit" :disabled="submitting">{{ t('sftp.keys.form.submit') }}</UiButton>
  </UiForm>
</template>
