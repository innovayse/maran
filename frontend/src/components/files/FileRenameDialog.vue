<script setup lang="ts">
/**
 * The rename dialog: one new name for an entry, which stays in its folder (moving is a different
 * dialog). The name is read client-side as advice only; the server re-validates and its
 * already-localized message is shown above the form. Dumb by the usual contract.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import { fileNameProblem } from '../../utils/fileName'

/** Props accepted by {@link FileRenameDialog}. */
const props = defineProps<{
  /** The entry's current name, which the field starts with. */
  currentName: string
  /** Whether the rename request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed rename, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileRenameDialog}. */
const emit = defineEmits<{
  /** Fired only when the name passes the client-side reading, carrying the trimmed name. */
  (e: 'submit', newName: string): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The form's id, which the footer's submit button names through its `form` attribute. */
const formId: string = useId()

/** The new name as typed. */
const name: Ref<string> = ref(props.currentName)

/** Whether a submit was attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** The validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  if (!submitted.value) {
    return null
  }
  const trimmed = name.value.trim()
  if (trimmed === props.currentName) {
    return t('filesDialogs.rename.unchanged')
  }
  const problem = fileNameProblem(trimmed)
  return problem === null ? null : t(`filesDialogs.nameProblems.${problem}`)
})

/**
 * Emits the name when it is not obviously wrong.
 * @returns Nothing.
 */
const submit = (): void => {
  submitted.value = true
  if (nameError.value === null) {
    emit('submit', name.value.trim())
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('filesDialogs.rename.title', { name: currentName })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <UiForm :id="formId" class="flex flex-col gap-4" @submit="submit">
      <UiInput v-model="name" :label="t('filesDialogs.rename.newName')" :error="nameError" mono required />
    </UiForm>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId" :disabled="submitting">{{ t('filesDialogs.rename.submit') }}</UiButton>
    </template>
  </UiModal>
</template>
