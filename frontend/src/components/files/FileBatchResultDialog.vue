<script setup lang="ts">
/**
 * The notice a batch leaves behind when some items failed: what worked and what did not, with the
 * server's own reasons. Dumb by the usual contract.
 */
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiModal from '../ui/UiModal.vue'
import FileBatchResultNotice from './FileBatchResultNotice.vue'
import type { FileBatchResult } from '../../types/file'

/** Props accepted by {@link FileBatchResultDialog}. */
defineProps<{
  /** The batch outcome to show. */
  result: FileBatchResult
}>()

/** Events emitted by {@link FileBatchResultDialog}. */
const emit = defineEmits<{
  /** The person dismissed the notice. */
  (e: 'close'): void
}>()

const { t } = useI18n()
</script>

<template>
  <UiModal :open="true" :title="t('filesDialogs.batch.title')" :close-label="t('common.close')" @close="emit('close')">
    <FileBatchResultNotice :result="result" />
    <template #footer>
      <UiButton @click="emit('close')">{{ t('common.done') }}</UiButton>
    </template>
  </UiModal>
</template>
