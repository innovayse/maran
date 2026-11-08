<script setup lang="ts">
/**
 * Says how a batch ended when not every item succeeded: how many worked and, for each that did not,
 * its name and the server's own reason. The reasons arrive already localized and are drawn as text,
 * never as markup (an entry name is customer-chosen). Dumb: it only renders what it is given.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import type { FileBatchItemResult, FileBatchResult } from '../../types/file'

/** Props accepted by {@link FileBatchResultNotice}. */
const props = defineProps<{
  /** The batch's outcome, one item per requested entry. */
  result: FileBatchResult
}>()

const { t } = useI18n()

/** The items that were not done. */
const failed: ComputedRef<FileBatchItemResult[]> = computed(() => {
  return props.result.items.filter((item) => {return !item.succeeded})
})

/** How many items were done. */
const succeededCount: ComputedRef<number> = computed(() => {
  return props.result.items.length - failed.value.length
})
</script>

<template>
  <UiAlert :variant="failed.length === 0 ? 'success' : 'warning'" data-testid="batch-result">
    <p>{{ t('filesDialogs.batch.summary', { done: succeededCount, failed: failed.length }) }}</p>
    <ul v-if="failed.length > 0" class="mt-2 flex flex-col gap-1">
      <li v-for="item in failed" :key="item.subject" class="wrap-anywhere">
        <span class="font-mono">{{ item.subject }}</span>
        <span v-if="item.message !== null">: {{ item.message }}</span>
      </li>
    </ul>
  </UiAlert>
</template>
