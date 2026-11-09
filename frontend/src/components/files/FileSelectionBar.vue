<script setup lang="ts">
/**
 * The bar above the listing once rows are ticked: how many, and the actions that apply to all of
 * them. It only reports which action was chosen; the dialogs and requests behind them are not its
 * business.
 *
 * The batch cap comes from the server's options. A selection over it disables the actions and says
 * why, but that is advice: the server enforces the cap on every request whatever the bar shows, and
 * while the cap is unknown the actions stay enabled.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiBulkBar from '../ui/UiBulkBar.vue'
import UiButton from '../ui/UiButton.vue'
import type { FileBatchAction } from '../../types/file'

/** Props accepted by {@link FileSelectionBar}. */
const props = defineProps<{
  /** How many rows are ticked; the bar is hidden at zero. */
  count: number
  /** The most items one batch may name, or `null` while the server has not said. */
  maxItems: number | null
}>()

/** Events emitted by {@link FileSelectionBar}. */
const emit = defineEmits<{
  /** An action was chosen for the ticked rows. */
  (e: 'action', action: FileBatchAction): void
  /** The selection was dismissed. */
  (e: 'clear'): void
}>()

const { t } = useI18n()

/** Whether the selection is larger than one batch may be. */
const overCap: ComputedRef<boolean> = computed(() => {
  return props.maxItems !== null && props.count > props.maxItems
})
</script>

<template>
  <div class="flex flex-col gap-2">
    <UiBulkBar
      :count="count"
      :label="t('filesBrowser.selection.count', { count }, count)"
      :actions-label="t('filesBrowser.selection.actions')"
    >
      <UiButton variant="secondary" :disabled="overCap" @click="emit('action', 'move')">{{ t('filesBrowser.menu.move') }}</UiButton>
      <UiButton variant="secondary" :disabled="overCap" @click="emit('action', 'copy')">{{ t('filesBrowser.menu.copy') }}</UiButton>
      <UiButton variant="secondary" :disabled="overCap" @click="emit('action', 'archive')">{{ t('filesBrowser.menu.archive') }}</UiButton>
      <UiButton variant="secondary" :disabled="overCap" @click="emit('action', 'trash')">{{ t('filesBrowser.menu.trash') }}</UiButton>
      <UiButton variant="destructive" :disabled="overCap" @click="emit('action', 'delete')">{{ t('filesBrowser.menu.delete') }}</UiButton>
      <UiButton variant="ghost" @click="emit('clear')">{{ t('filesBrowser.selection.clear') }}</UiButton>
    </UiBulkBar>
    <UiAlert v-if="overCap" variant="warning">{{ t('filesBrowser.selection.tooMany', { max: maxItems ?? 0 }) }}</UiAlert>
  </div>
</template>
