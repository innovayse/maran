<script setup lang="ts">
/**
 * The upload queue: every file the person has added to this session, with its state and actions,
 * and the server's size limit as a hint. It renders nothing while the queue is empty, so a screen
 * can always mount it.
 *
 * The drop zone and file picker that fill the queue belong to the screen that knows the folder
 * (it calls the transfer store's `enqueue`); this component only shows and steers what is queued.
 * It announces `finished` each time an upload lands, so the screen reads the folder again without
 * a store reaching into another store (rules/vue.md).
 */
import { computed, watch, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import FileUploadItem from './FileUploadItem.vue'
import { useFileTransfersStore } from '../../stores/fileTransfers'
import { formatBytes } from '../../utils/formatBytes'

/** Events emitted by {@link FileUploadQueue}. */
const emit = defineEmits<{
  /** Fired when an upload has landed in its folder, so the listing should be read again. */
  (e: 'finished'): void
}>()

const { t } = useI18n()
const store = useFileTransfersStore()

/** The limit sentence, or `null` before the server's limits are known. */
const limitHint: ComputedRef<string | null> = computed(() => {
  return store.options === null
    ? null
    : t('filesTransfers.limitHint', { limit: formatBytes(store.options.maxUploadBytes) })
})

/** Whether any row is over and can be cleared. */
const hasFinished: ComputedRef<boolean> = computed(() => {
  return store.items.some((item) => {return item.state !== 'queued' && item.state !== 'uploading'})
})

watch(
  () => {return store.completedCount},
  (): void => {
    emit('finished')
  },
)
</script>

<template>
  <UiCard v-if="store.items.length > 0">
    <section class="flex flex-col gap-2" :aria-label="t('filesTransfers.queueLabel')">
      <div class="flex flex-wrap items-center gap-2">
        <h2 class="flex-1 text-lg font-bold text-text-primary">{{ t('filesTransfers.queueHeading') }}</h2>
        <span v-if="limitHint !== null" class="text-sm text-text-muted">{{ limitHint }}</span>
        <UiButton v-if="hasFinished" variant="ghost" @click="store.dismissFinished()">{{
          t('filesTransfers.clearFinished')
        }}</UiButton>
      </div>
      <ul class="flex flex-col divide-y divide-border-subtle">
        <FileUploadItem
          v-for="item in store.items"
          :key="item.id"
          :item="item"
          :can-replace="store.refusedForExisting(item.id)"
          @cancel="store.cancel"
          @retry="store.retry($event)"
          @replace="store.retry($event, true)"
          @dismiss="store.dismiss"
        />
      </ul>
    </section>
  </UiCard>
</template>
