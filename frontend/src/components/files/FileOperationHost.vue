<script setup lang="ts">
/**
 * Shows the dialog the operation the file browser asked for needs and hands it on to the runner.
 * The browser (a row's menu, the selection bar, the toolbar) only says WHAT was asked as a
 * {@link FileOperationOrder}; everything about how it is asked is here and how it is run is in
 * `useFileOperationRunner`, so the page stays a composition of parts. The destination picker of
 * move, copy and extract is driven by `useFolderPicker`, which pages and searches on the server.
 *
 * Nothing here decides what is allowed: names are read client-side as advice only, and the server
 * and the agent are the authority.
 */
import { computed, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import FileArchiveDialog from './FileArchiveDialog.vue'
import FileBatchResultDialog from './FileBatchResultDialog.vue'
import FileDeleteConfirm from './FileDeleteConfirm.vue'
import FileExtractDialog from './FileExtractDialog.vue'
import FileMoveCopyDialog from './FileMoveCopyDialog.vue'
import FileNewEntryDialog from './FileNewEntryDialog.vue'
import FilePermissionsDialog from './FilePermissionsDialog.vue'
import FileRenameDialog from './FileRenameDialog.vue'
import FileTrashDialog from './FileTrashDialog.vue'
import { useFileOperationRunner, type FileOperationOrder } from '../../composables/useFileOperationRunner'
import { useFolderPicker } from '../../composables/useFolderPicker'
import { useFilesOptionsStore } from '../../stores/filesOptions'
import { formatBytes } from '../../utils/formatBytes'
import { baseName } from '../../utils/filePath'
import type { ArchiveFormat } from '../../types/file'

/** Props accepted by {@link FileOperationHost}. */
const props = defineProps<{
  /** The operation to carry out, or `null` while none is asked. */
  order: FileOperationOrder | null
  /** The account whose files are acted on. */
  accountId: string
  /** The folder being browsed, relative to the home: where new entries and archives land. */
  folder: string
}>()

/** Events emitted by {@link FileOperationHost}. */
const emit = defineEmits<{
  /** The operation changed something, so the listing should be read again. */
  (e: 'done'): void
  /** The operation is over, whatever its outcome; the page forgets the order. */
  (e: 'close'): void
}>()

const { t } = useI18n()
const optionsStore = useFilesOptionsStore()

// The runner reports through the page's events: `done` when the listing changed, `close` when the order is over.
const runner = useFileOperationRunner(
  props,
  () => {
    emit('done')
  },
  () => {
    emit('close')
  },
)
const picker = useFolderPicker(props)

/** The archive formats the server offers. */
const formats: ComputedRef<ArchiveFormat[]> = computed(() => {
  return optionsStore.options?.archiveFormats ?? []
})

/** The sentence stating the server's extraction limits, or `null` while they are unknown. */
const limitsHint: ComputedRef<string | null> = computed(() => {
  const options = optionsStore.options
  if (options === null) {
    return null
  }
  return t('filesDialogs.extract.limits', {
    entries: options.extractMaxEntries,
    size: formatBytes(options.extractMaxBytes),
    ratio: options.extractMaxRatio,
  })
})

/** The trash retention in days, once the server has said. */
const retentionDays: ComputedRef<number | null> = computed(() => {
  return optionsStore.options?.trashRetentionDays ?? null
})

/** The name an archive starts with: the one entry's name, else the folder's, else a word. */
const suggestedArchiveName: ComputedRef<string> = computed(() => {
  if (runner.entries.value.length === 1 && runner.first.value !== null) {
    return runner.first.value.name
  }
  const folderName = baseName(props.folder)
  return folderName === '' ? t('filesDialogs.archive.defaultName') : folderName
})

// A new order starts clean: no old failure, no old listing, and whatever it needs read first.
watch(
  () => {return props.order},
  (order) => {
    runner.begin(order)
    if (order?.action === 'move' || order?.action === 'copy' || order?.action === 'extract') {
      void picker.browse(props.folder)
    }
  },
  { immediate: true },
)
</script>

<template>
  <FileNewEntryDialog
    v-if="runner.action.value === 'createFile' || runner.action.value === 'createFolder'"
    :kind="runner.action.value === 'createFile' ? 'file' : 'directory'"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @submit="runner.create"
    @close="runner.close"
  />
  <FileRenameDialog
    v-else-if="runner.action.value === 'rename' && runner.first.value !== null"
    :current-name="runner.first.value.name"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @submit="runner.rename"
    @close="runner.close"
  />
  <FilePermissionsDialog
    v-else-if="runner.action.value === 'permissions' && runner.first.value !== null"
    :name="runner.first.value.name"
    :current-mode="runner.first.value.mode"
    :is-directory="runner.first.value.kind === 'directory'"
    :owner-text="runner.ownerText.value"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @submit="runner.changePermissions"
    @close="runner.close"
  />
  <FileMoveCopyDialog
    v-else-if="runner.action.value === 'move' || runner.action.value === 'copy'"
    :mode="runner.action.value"
    :count="runner.entries.value.length"
    :start-path="folder"
    :listing="picker.listing.value"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @browse="picker.browse"
    @search="picker.find"
    @more="picker.more"
    @submit="runner.transfer"
    @close="runner.close"
  />
  <FileArchiveDialog
    v-else-if="runner.action.value === 'archive'"
    :count="runner.entries.value.length"
    :suggested-name="suggestedArchiveName"
    :formats="formats"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @submit="runner.archive"
    @close="runner.close"
  />
  <FileExtractDialog
    v-else-if="runner.action.value === 'extract' && runner.first.value !== null"
    :name="runner.first.value.name"
    :start-path="folder"
    :listing="picker.listing.value"
    :limits-hint="limitsHint"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @browse="picker.browse"
    @search="picker.find"
    @more="picker.more"
    @submit="runner.extract"
    @close="runner.close"
  />
  <FileDeleteConfirm
    v-else-if="runner.action.value === 'delete'"
    :count="runner.entries.value.length"
    :name="runner.entries.value.length === 1 && runner.first.value !== null ? runner.first.value.name : null"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @confirm="runner.remove"
    @close="runner.close"
  />
  <FileTrashDialog
    v-else-if="runner.action.value === 'trash'"
    :count="runner.entries.value.length"
    :retention-days="retentionDays"
    :submitting="runner.submitting.value"
    :error-message="runner.errorMessage.value"
    @confirm="runner.trash"
    @close="runner.close"
  />
  <FileBatchResultDialog v-if="runner.result.value !== null" :result="runner.result.value" @close="runner.close" />
</template>
