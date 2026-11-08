<script setup lang="ts">
/**
 * The compress dialog: a name and a format for an archive written in the folder being browsed. The
 * formats are the closed set the server names in its options. The name is read client-side as advice
 * only; the server re-validates. Dumb by the usual contract.
 */
import { computed, ref, useId, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiForm from '../ui/UiForm.vue'
import UiInput from '../ui/UiInput.vue'
import UiModal from '../ui/UiModal.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import { fileNameProblem } from '../../utils/fileName'
import type { ArchiveFormat } from '../../types/file'

/** Props accepted by {@link FileArchiveDialog}. */
const props = defineProps<{
  /** How many entries go into the archive. */
  count: number
  /** The name the field starts with, without an extension. */
  suggestedName: string
  /** The formats the server offers; the first is preselected. */
  formats: readonly ArchiveFormat[]
  /** Whether the request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed request, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileArchiveDialog}. */
const emit = defineEmits<{
  /** Fired only when the name passes the client-side reading: the archive's file name and format. */
  (e: 'submit', fileName: string, format: ArchiveFormat): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The i18n key of each format's label; a format such as `tar.gz` is not a usable key itself. */
const FORMAT_KEYS: Readonly<Record<ArchiveFormat, string>> = { zip: 'zip', 'tar.gz': 'tarGz' }

/** The form's id, which the footer's submit button names through its `form` attribute. */
const formId: string = useId()

/** The name as typed. */
const name: Ref<string> = ref(props.suggestedName)

/** The format chosen. */
const format: Ref<ArchiveFormat> = ref(props.formats[0] ?? 'zip')

/** Whether a submit was attempted, so nothing turns red before the person has tried. */
const submitted: Ref<boolean> = ref(false)

/** The choices of the format select. */
const options: ComputedRef<SelectOption[]> = computed(() => {
  return props.formats.map((value) => {
    return { value, label: t(`filesDialogs.archive.formats.${FORMAT_KEYS[value]}`) }
  })
})

/** The file name that will be written: the typed name, given the format's extension unless it has it. */
const fileName: ComputedRef<string> = computed(() => {
  const trimmed = name.value.trim()
  return trimmed.endsWith(`.${format.value}`) ? trimmed : `${trimmed}.${format.value}`
})

/** The validation message for the name, or `null`. */
const nameError: ComputedRef<string | null> = computed(() => {
  const problem = submitted.value ? fileNameProblem(name.value.trim()) : null
  return problem === null ? null : t(`filesDialogs.nameProblems.${problem}`)
})

/**
 * Emits the file name and format when the name is not obviously wrong.
 * @returns Nothing.
 */
const submit = (): void => {
  submitted.value = true
  if (fileNameProblem(name.value.trim()) === null) {
    emit('submit', fileName.value, format.value)
  }
}

/**
 * Takes a format chosen in the select.
 * @param value The option value.
 * @returns Nothing.
 */
const chooseFormat = (value: string): void => {
  const chosen = props.formats.find((candidate) => {return candidate === value})
  if (chosen !== undefined) {
    format.value = chosen
  }
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('filesDialogs.archive.title', { count }, count)"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <UiForm :id="formId" class="flex flex-col gap-4" @submit="submit">
      <UiInput v-model="name" :label="t('filesDialogs.archive.name')" :error="nameError" mono required />
      <UiSelect
        :model-value="format"
        :label="t('filesDialogs.archive.format')"
        :options="options"
        @update:model-value="chooseFormat"
      />
      <UiFieldHint>{{ t('filesDialogs.archive.willWrite', { name: fileName }) }}</UiFieldHint>
    </UiForm>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton type="submit" :form="formId" :disabled="submitting">{{ t('filesDialogs.archive.submit') }}</UiButton>
    </template>
  </UiModal>
</template>
