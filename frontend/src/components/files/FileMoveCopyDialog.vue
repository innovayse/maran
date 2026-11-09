<script setup lang="ts">
/**
 * The move and copy dialog: pick the destination folder by walking the folders and confirm. The two
 * actions differ only in their words, so they share this one dialog. Dumb by the usual contract:
 * the listing arrives as props, a wish to look elsewhere leaves as `browse`.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiModal from '../ui/UiModal.vue'
import FileFolderPicker, { type FolderListing } from './FileFolderPicker.vue'

/** Props accepted by {@link FileMoveCopyDialog}. */
const props = defineProps<{
  /** Whether the entries are moved or copied. */
  mode: 'move' | 'copy'
  /** How many entries the action is for. */
  count: number
  /** The folder the picker starts in. */
  startPath: string
  /** The listing of the folder being looked at. */
  listing: FolderListing
  /** Whether the request is in flight, which disables submit. */
  submitting: boolean
  /** The panel's message for the last failed request, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link FileMoveCopyDialog}. */
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

/** The dialog's title for the action, counted. Both keys are spelled out so the locale check can see them. */
const title: ComputedRef<string> = computed(() => {
  return props.mode === 'move'
    ? t('filesDialogs.move.title', { count: props.count }, props.count)
    : t('filesDialogs.copy.title', { count: props.count }, props.count)
})

/** The confirm button's words for the action. */
const submitLabel: ComputedRef<string> = computed(() => {
  return props.mode === 'move' ? t('filesDialogs.move.submit') : t('filesDialogs.copy.submit')
})

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
    :title="title"
    :close-label="t('common.close')"
    @close="emit('close')"
  >
    <UiAlert v-if="errorMessage !== null" variant="error" class="mb-4">{{ errorMessage }}</UiAlert>
    <FileFolderPicker
      :path="path"
      :listing="listing"
      @browse="browse"
      @search="emit('search', $event)"
      @more="emit('more')"
    />
    <template #footer>
      <UiButton variant="secondary" @click="emit('close')">{{ t('common.cancel') }}</UiButton>
      <UiButton :disabled="submitting" @click="emit('submit', path)">{{ submitLabel }}</UiButton>
    </template>
  </UiModal>
</template>
