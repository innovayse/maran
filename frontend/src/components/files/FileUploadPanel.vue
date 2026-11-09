<script setup lang="ts">
/**
 * The upload side of the file browser: the drop zone with the size limit the server reports, the
 * queue of files going up, and the reason a download failed. Files are queued through the transfers
 * store into the folder being browsed; the uploads then continue on their own and `finished` tells
 * the page to read the folder again.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiDropZone from '../ui/UiDropZone.vue'
import FileUploadQueue from './FileUploadQueue.vue'
import { useFileTransfersStore } from '../../stores/fileTransfers'
import { useFilesOptionsStore } from '../../stores/filesOptions'
import { formatBytes } from '../../utils/formatBytes'

/** Props accepted by {@link FileUploadPanel}. */
const props = defineProps<{
  /** The account whose home receives the files. */
  accountId: string
  /** The folder being browsed, relative to the home: where the files land. */
  folder: string
}>()

/** Events emitted by {@link FileUploadPanel}. */
const emit = defineEmits<{
  /** An upload finished, so the listing should be read again. */
  (e: 'finished'): void
}>()

const { t } = useI18n()
const optionsStore = useFilesOptionsStore()
const transfers = useFileTransfersStore()

/**
 * Queues files the person dropped or chose for upload into the folder being browsed.
 * @param files The files.
 * @returns Resolves once the files are queued; the uploads continue on their own.
 */
const onFilesSelected = async (files: File[]): Promise<void> => {
  await transfers.enqueue(props.accountId, props.folder, files)
}
</script>

<template>
  <UiDropZone
    :label="t('filesTransfers.dropLabel')"
    :pick-label="t('filesTransfers.pickLabel')"
    :hint="
      optionsStore.options === null
        ? undefined
        : t('filesTransfers.limitHint', { limit: formatBytes(optionsStore.options.maxUploadBytes) })
    "
    @select="onFilesSelected"
  />
  <FileUploadQueue @finished="emit('finished')" />
  <UiAlert v-if="transfers.downloadErrorMessage !== null" variant="error">
    {{ transfers.downloadErrorMessage }}
  </UiAlert>
</template>
