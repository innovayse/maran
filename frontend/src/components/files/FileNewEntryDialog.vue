<script setup lang="ts">
/**
 * The new-file or new-folder dialog: one name, created inside the folder being browsed. It holds the
 * draft and the client-side reading of the name (advice that saves a round trip; the server
 * re-validates and its already-localized message is shown above the form). Dumb by the usual
 * contract: it never touches a store or the API layer, and emits only a name that is not obviously wrong.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import { fileNameProblem } from '../../utils/fileName'

/** Props accepted by {@link FileNewEntryDialog}. */
defineProps<{
  /** Whether an empty file or a folder is created. */
  kind: 'file' | 'directory'
  /** Whether the create request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed create, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileNewEntryDialog}. */
const emit = defineEmits<{
  /** Fired only when the name passes the client-side reading, carrying the trimmed name. */
  (e: 'submit', name: string): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The form's id, which the footer's submit button names through its `form` attribute. */
const formId: string = useId()

/** The name as typed. */
const name: Ref<string> = ref('')

/** Whether a submit was attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** The validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  const problem = submitted.value ? fileNameProblem(name.value.trim()) : null
  return problem === null ? null : t(`filesDialogs.nameProblems.${problem}`)
})

/**
 * Emits the name when it is not obviously wrong.
 * @returns Nothing.
 */
const submit = (): void => {
  submitted.value = true
  if (fileNameProblem(name.value.trim()) === null) {
    emit('submit', name.value.trim())
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="kind === 'file' ? t('filesDialogs.newFile.title') : t('filesDialogs.newFolder.title')"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <UiForm :id="formId" class="flex flex-col gap-4" @submit="submit">
      <UiInput v-model="name" :label="t('filesDialogs.nameLabel')" :error="nameError" mono required />
    </UiForm>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId" :disabled="submitting">{{ t('filesDialogs.create') }}</UiButton>
    </template>
  </UiModal>
</template>
