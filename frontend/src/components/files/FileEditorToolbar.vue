<script setup lang="ts">
/**
 * The editor's action bar: save, reload from disk, download the stored version, the line-wrapping
 * switch, and the size counter against the server's limit. State in, events out; the page wires it
 * to the editor store.
 *
 * The counter turns to the danger colour when the text is over the limit, and the message beside
 * it says what to do, so colour is never the only carrier. A saved file shows a polite "Saved"
 * status that the next keystroke retires.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiSwitch from '../ui/UiSwitch.vue'
import FileDownloadButton from './FileDownloadButton.vue'
import { formatBytes } from '../../utils/formatBytes'
import { baseName } from '../../utils/filePath'

/** Props accepted by {@link FileEditorToolbar}. */
const props = defineProps<{
  /** The account that owns the file, for the download. */
  accountId: string
  /** The open file's path, for the download. */
  path: string
  /** Whether saving is allowed right now (a change, within the limit, none in flight). */
  canSave: boolean
  /** Whether a save request is in flight. */
  saving: boolean
  /** Whether the latest save succeeded and nothing was typed since. */
  justSaved: boolean
  /** The edited text's size in bytes. */
  sizeBytes: number
  /** The server's edit limit in bytes, or `null` while it is not known. */
  limitBytes: number | null
  /** Whether the text is over the limit. */
  overLimit: boolean
  /** The file's permission bits, shown read-only. */
  mode: string
}>()

/** Events emitted by {@link FileEditorToolbar}. */
const emit = defineEmits<{
  /** The person asks to save. */
  (e: 'save'): void
  /** The person asks to read the file again from disk. */
  (e: 'reload'): void
}>()

/** Whether long lines stay on one line (`v-model` target). */
const noWrap = defineModel<boolean>('noWrap', { required: true })

const { t } = useI18n()

/** The size counter text. */
const sizeText: ComputedRef<string> = computed(() => {
  return props.limitBytes === null
    ? t('filesEditor.sizeOnly', { size: formatBytes(props.sizeBytes) })
    : t('filesEditor.size', { size: formatBytes(props.sizeBytes), limit: formatBytes(props.limitBytes) })
})

/** What to do about an oversized text, or `null` when it fits. */
const overLimitText: ComputedRef<string | null> = computed(() => {
  return props.overLimit && props.limitBytes !== null
    ? t('filesEditor.overLimit', { limit: formatBytes(props.limitBytes) })
    : null
})
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex flex-wrap items-center gap-2">
      <UiButton :disabled="!canSave" @click="emit('save')">{{
        saving ? t('filesEditor.saving') : t('filesEditor.save')
      }}</UiButton>
      <UiButton variant="secondary" :disabled="saving" @click="emit('reload')">{{ t('filesEditor.reload') }}</UiButton>
      <FileDownloadButton :account-id="accountId" :path="path" :name="baseName(path)" />
      <UiSwitch v-model="noWrap" :label="t('filesEditor.noWrap')" />
      <span v-if="justSaved" role="status" class="text-sm text-text-secondary">{{ t('filesEditor.saved') }}</span>
    </div>
    <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
      <span class="font-mono" :class="overLimit ? 'text-danger' : 'text-text-secondary'">{{ sizeText }}</span>
      <span v-if="overLimitText !== null" class="text-danger">{{ overLimitText }}</span>
      <span class="text-text-muted">{{ t('filesEditor.mode', { mode }) }}</span>
    </div>
  </div>
</template>
