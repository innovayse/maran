<script setup lang="ts">
/**
 * The permanent-delete confirmation. Unlike moving to the trash, this cannot be undone, so the
 * person must tick an explicit acknowledgement before the destructive button works. The server
 * refuses a delete that was not confirmed whatever this dialog does. Dumb by the usual contract.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCheckbox from '../ui/UiCheckbox.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiModal from '../ui/UiModal.vue'
import UiSpinner from '../ui/UiSpinner.vue'

/** Props accepted by {@link FileDeleteConfirm}. */
defineProps<{
  /** How many entries would be deleted. */
  count: number
  /** The single entry's name, or `null` for several, so the sentence can name it. */
  name: string | null
  /** Whether the request is in flight; the dialog then shows only a spinner. */
  submitting: boolean
  /** The panel's message for the last failed request, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileDeleteConfirm}. */
const emit = defineEmits<{
  /** The person confirmed with the acknowledgement ticked. */
  (e: 'confirm'): void
  /** The person declined. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** Whether the person ticked that this cannot be undone. */
const acknowledged: Ref<boolean> = ref(false)
</script>

<template>
  <UiModal
    :open="true"
    :title="t('filesDialogs.delete.title', { count }, count)"
    :close-label="t('common.close')"
    :dismissible="!submitting"
    @close="emit('close')"
  >
    <UiSpinner v-if="submitting" :label="t('filesDialogs.delete.working')" />
    <div v-else class="flex flex-col gap-4">
      <UiAlert v-if="errorMessage !== null" variant="error">{{ errorMessage }}</UiAlert>
      <div class="flex items-start gap-2.5">
        <UiIcon name="triangleAlert" size="sm" class="mt-0.5 shrink-0 text-danger" />
        <p class="min-w-0 wrap-anywhere" data-testid="confirm-message">
          {{ name === null ? t('filesDialogs.delete.questionMany', { count }, count) : t('filesDialogs.delete.questionOne', { name }) }}
        </p>
      </div>
      <UiCheckbox v-model="acknowledged" :label="t('filesDialogs.delete.acknowledge')" />
    </div>
    <template #footer>
      <template v-if="!submitting">
        <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
        <UiButton variant="destructive" :disabled="!acknowledged" @click="emit('confirm')">
          {{ t('filesDialogs.delete.confirm') }}
        </UiButton>
      </template>
    </template>
  </UiModal>
</template>
