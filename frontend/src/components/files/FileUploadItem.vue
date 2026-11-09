<script setup lang="ts">
/**
 * One row of the upload queue: the file's name and size, where it stands, and the actions that
 * make sense for that state: cancel while it waits or runs, try again or replace after a refusal,
 * remove once it is over. A refusal shows the server's own sentence, or the panel's fallback when
 * no answer arrived at all.
 *
 * It is dumb: state in, events out. The queue wires the events to the transfer store.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiBadge, { type BadgeVariant } from '../ui/UiBadge.vue'
import UiButton from '../ui/UiButton.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { formatBytes } from '../../utils/formatBytes'
import type { UploadItem } from '../../types/file'

/** Props accepted by {@link FileUploadItem}. */
const props = defineProps<{
  /** The queue item shown. */
  item: UploadItem
  /** Whether a file of the same name exists, so "replace" is offered as the way past the refusal. */
  canReplace: boolean
}>()

/** Events emitted by {@link FileUploadItem}. */
const emit = defineEmits<{
  /** The person cancels a waiting or running upload. */
  (e: 'cancel', id: string): void
  /** The person tries a failed or cancelled upload again. */
  (e: 'retry', id: string): void
  /** The person re-sends the upload over the existing file. */
  (e: 'replace', id: string): void
  /** The person removes a finished row. */
  (e: 'dismiss', id: string): void
}>()

const { t } = useI18n()

/** The badge tone for the item's state. */
const tone: ComputedRef<BadgeVariant> = computed(() => {
  switch (props.item.state) {
    case 'done':
      return 'success'
    case 'failed':
      return 'danger'
    case 'uploading':
      return 'info'
    default:
      return 'neutral'
  }
})

/** Whether the upload is still going to happen, so it can be cancelled. */
const active: ComputedRef<boolean> = computed(() => {
  return props.item.state === 'queued' || props.item.state === 'uploading'
})

/** What a failed row says: the server's sentence, or the fallback when none came. */
const failureText: ComputedRef<string> = computed(() => {
  return props.item.error ?? t('filesTransfers.failedNoAnswer')
})
</script>

<template>
  <li class="flex flex-col gap-1 py-2">
    <div class="flex flex-wrap items-center gap-2">
      <span class="min-w-0 flex-1 font-mono text-sm wrap-anywhere text-text-primary">{{ item.file.name }}</span>
      <span class="text-xs text-text-muted">{{ formatBytes(item.file.size) }}</span>
      <UiBadge :variant="tone">{{ t(`filesTransfers.state.${item.state}`) }}</UiBadge>
      <UiSpinner v-if="item.state === 'uploading'" :label="t('filesTransfers.uploadingLabel', { name: item.file.name })" />
    </div>
    <p v-if="item.state === 'failed'" class="text-sm text-danger wrap-anywhere">{{ failureText }}</p>
    <div class="flex flex-wrap gap-2">
      <UiButton
        v-if="active"
        variant="ghost"
        :aria-label="t('filesTransfers.cancelLabel', { name: item.file.name })"
        @click="emit('cancel', item.id)"
        >{{ t('filesTransfers.cancel') }}</UiButton
      >
      <UiButton
        v-if="item.state === 'failed' || item.state === 'cancelled'"
        variant="secondary"
        :aria-label="t('filesTransfers.retryLabel', { name: item.file.name })"
        @click="emit('retry', item.id)"
        >{{ t('filesTransfers.retry') }}</UiButton
      >
      <UiButton
        v-if="item.state === 'failed' && canReplace"
        variant="secondary"
        :aria-label="t('filesTransfers.replaceLabel', { name: item.file.name })"
        @click="emit('replace', item.id)"
        >{{ t('filesTransfers.replace') }}</UiButton
      >
      <UiButton
        v-if="!active"
        variant="ghost"
        :aria-label="t('filesTransfers.dismissLabel', { name: item.file.name })"
        @click="emit('dismiss', item.id)"
        >{{ t('filesTransfers.dismiss') }}</UiButton
      >
    </div>
  </li>
</template>
