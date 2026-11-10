<script setup lang="ts">
/**
 * The import's first step: the package file and the fingerprint of the key that signed it. The
 * fingerprint is read on the sending server and typed here, so the panel accepts the package only if it
 * was signed by the key the administrator saw there; a package from any other server is refused before
 * anything is created.
 *
 * Dumb by the usual contract: props in, emits out. The checks are advice (a file and a fingerprint must
 * be given); the panel verifies the signature and its localized refusal is what the administrator reads.
 * The button says what the wait will be like: the upload is one request without progress, and a large
 * package takes a while.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiFileInput from '../ui/UiFileInput.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'

/** Props accepted by {@link AccountImportUploadForm}. */
defineProps<{
  /** Whether the upload is in flight, which disables the submit control. */
  submitting: boolean
}>()

/** Events emitted by {@link AccountImportUploadForm}. */
const emit = defineEmits<{
  /** Fired only when a file and a fingerprint were given. */
  (e: 'submit', file: File, fingerprint: string): void
}>()

const { t } = useI18n()

/** The package the administrator chose, or `null` before a choice. */
const file: Ref<File | null> = ref(null)

/** The fingerprint as typed or pasted. */
const fingerprint: Ref<string> = ref('')

/** Whether a submit has been attempted, so nothing turns red before the administrator has tried. */
const submitted: Ref<boolean> = ref(false)

/** Validation message for the fingerprint, or `null`. */
const fingerprintError: ComputedRef<string | null> = computed(() => {
  return submitted.value && fingerprint.value.trim() === '' ? t('accountMove.import.form.fingerprintRequired') : null
})

/** Validation message for the file, shown as a line under the chooser, or `null`. */
const fileError: ComputedRef<string | null> = computed(() => {
  return submitted.value && file.value === null ? t('accountMove.import.form.fileRequired') : null
})

/**
 * Remembers the chosen file.
 * @param chosen The file, or `null` when the choice was cleared.
 * @returns Nothing.
 */
const onSelect = (chosen: File | null): void => {
  file.value = chosen
}

/**
 * Validates, then emits the file and the trimmed fingerprint.
 * @returns Nothing; emits synchronously when both were given.
 */
const submit = (): void => {
  submitted.value = true
  if (file.value !== null && fingerprint.value.trim() !== '') {
    emit('submit', file.value, fingerprint.value.trim())
  }
}
</script>

<template>
  <UiCard>
    <UiForm class="flex flex-col gap-4" @submit="submit">
      <div class="flex max-w-xl flex-col gap-1.5">
        <UiFileInput :label="t('accountMove.import.form.file')" :disabled="submitting" @select="onSelect" />
        <p v-if="fileError !== null" class="text-sm text-danger" role="alert">{{ fileError }}</p>
        <p v-else class="text-sm text-text-muted">{{ t('accountMove.import.form.fileHint') }}</p>
      </div>
      <div class="flex max-w-xl flex-col gap-1.5">
        <UiInput
          v-model="fingerprint"
          :label="t('accountMove.import.form.fingerprint')"
          :error="fingerprintError"
          mono
          required
          autocomplete="off"
        />
        <p class="text-sm text-text-muted">{{ t('accountMove.import.form.fingerprintHint') }}</p>
      </div>
      <div class="flex flex-wrap items-center justify-between gap-x-6 gap-y-3">
        <p class="min-w-0 flex-1 basis-64 text-sm text-text-muted">{{ t('accountMove.import.form.hint') }}</p>
        <UiButton type="submit" :disabled="submitting">
          {{ submitting ? t('accountMove.import.form.submitting') : t('accountMove.import.form.submit') }}
        </UiButton>
      </div>
    </UiForm>
  </UiCard>
</template>
