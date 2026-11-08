<script setup lang="ts">
/**
 * The download action for one entry. A file is fetched and handed to the browser's save flow; a
 * folder is first packed into an archive written beside it and that archive is downloaded, so the
 * person ends with one file and the folder's parent gains the archive.
 *
 * It calls the transfer store and nothing else (rules/vue.md). Only one download runs at a time,
 * so every download button is disabled while one is in flight; the store's `downloadErrorMessage`
 * is shown by the screen that hosts the buttons, not here.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import { useFileTransfersStore } from '../../stores/fileTransfers'

/** Props accepted by {@link FileDownloadButton}. */
const props = defineProps<{
  /** The account that owns the entry. */
  accountId: string
  /** The entry's path relative to the home. */
  path: string
  /** The entry's own name, for the accessible name. */
  name: string
  /** Whether the entry is a folder, which is downloaded as an archive. */
  isDirectory?: boolean
}>()

/** Events emitted by {@link FileDownloadButton}. */
const emit = defineEmits<{
  /** Fired when a folder's archive was written, so the listing shows the new file. */
  (e: 'archived'): void
}>()

const { t } = useI18n()
const store = useFileTransfersStore()

/** Whether this button's own entry is the one being prepared. */
const working: ComputedRef<boolean> = computed(() => {
  return store.downloadingPath === props.path
})

/** The text on the button. */
const label: ComputedRef<string> = computed(() => {
  if (working.value) {
    return t('filesTransfers.downloading')
  }
  return props.isDirectory === true ? t('filesTransfers.downloadFolder') : t('filesTransfers.download')
})

/** The accessible name, which says which entry the button is for. */
const accessibleName: ComputedRef<string> = computed(() => {
  return props.isDirectory === true
    ? t('filesTransfers.downloadFolderLabel', { name: props.name })
    : t('filesTransfers.downloadLabel', { name: props.name })
})

/**
 * Starts the download.
 * @returns Resolves once the browser has the file or the attempt failed.
 */
const start = async (): Promise<void> => {
  if (props.isDirectory === true) {
    if (await store.downloadFolder(props.accountId, props.path)) {
      emit('archived')
    }
    return
  }
  await store.download(props.accountId, props.path)
}
</script>

<template>
  <UiButton
    variant="secondary"
    :disabled="store.downloadingPath !== null"
    :aria-label="accessibleName"
    @click="start"
  >
    {{ label }}
  </UiButton>
</template>
