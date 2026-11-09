<script setup lang="ts">
/**
 * The extract dialog: pick the folder an archive is unpacked into. The limits on what an extraction
 * may write (entries, bytes, ratio) are the agent's to enforce; this dialog only states the ones the
 * server reports. Dumb by the usual contract.
 */
import { ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiModal from '../ui/UiModal.vue'
import FileFolderPicker, { type FolderListing } from './FileFolderPicker.vue'

/** Props accepted by {@link FileExtractDialog}. */
const props = defineProps<{
  /** The archive's name, for the title. */
  name: string
  /** The folder the picker starts in. */
  startPath: string
  /** The listing of the folder being looked at. */
  listing: FolderListing
  /** The sentence stating the server's extraction limits, or `null` while they are unknown. */
  limitsHint: string | null
  /** Whether the request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed request, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileExtractDialog}. */
const emit = defineEmits<{
  /** The person wants to look inside another folder. */
  (e: 'browse', path: string): void
  /** The name search of the folder being looked at changed (empty clears it). */
  (e: 'search', term: string): void
  /** The person asked for the next page of folders. */
  (e: 'more'): void
  /** The person confirmed the folder being looked at as the destination. */
  (e: 'submit', destinationDirectory: string): void
  /** The person closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/** The folder being looked at, which is the destination. */
const path: Ref<string> = ref(props.startPath)

/**
 * Looks inside another folder and tells the host to list it.
 * @param next The folder to look inside.
 * @returns Nothing.
 */
const browse = (next: string): void => {
  path.value = next
  emit('browse', next)
}
</script>

<template>
  <UiModal
    :open="true"
    :title="t('filesDialogs.extract.title', { name })"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <div class="flex flex-col gap-3">
      <FileFolderPicker
        :path="path"
        :listing="listing"
        @browse="browse"
        @search="emit('search', $event)"
        @more="emit('more')"
      />
      <UiFieldHint v-if="limitsHint !== null">{{ limitsHint }}</UiFieldHint>
    </div>
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton :disabled="submitting" @click="emit('submit', path)">{{ t('filesDialogs.extract.submit') }}</UiButton>
    </template>
  </UiModal>
</template>
