<script setup lang="ts">
/**
 * The remote servers list's filter sheet: the state a server is in. The states are the closed set
 * the panel reports, named from the locale table. Props in, events out: the page owns the applied
 * filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { RemoteServerState } from '../../types/migrationSource'

/** Props accepted by {@link RemoteServersFilterSheet}. */
defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
}>()

/** Events emitted by {@link RemoteServersFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
}>()

/** The states a server can be in, in the order a server moves through them. */
const STATES: readonly RemoteServerState[] = [
  'hostKeyUnconfirmed',
  'pinned',
  'ready',
  'hostKeyChanged',
  'authRefused',
  'unreachable',
]

const { t } = useI18n()

/** The state choices, led by "any". */
const stateOptions: ComputedRef<SelectOption[]> = computed(() => {
  const named = STATES.map((state) => {
    return { value: state, label: t(`migration.states.${state}`) }
  })
  return [{ value: '', label: t('common.list.any') }, ...named]
})

/**
 * Forwards the applied draft.
 * @param filters The draft the reader applied.
 * @returns Nothing; the event is emitted synchronously.
 */
const apply = (filters: Record<string, string>): void => {
  emit('apply', filters)
}

/**
 * Forwards a close.
 * @returns Nothing; the event is emitted synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiFilterSheet
    :open="open"
    :model-value="filters"
    :title="t('common.list.filters')"
    :close-label="t('common.close')"
    :apply-label="t('common.list.apply')"
    :reset-label="t('common.list.reset')"
    @update:model-value="apply"
    @close="close"
  >
    <template #default="{ draft }">
      <UiSelect v-model="draft.state" :label="t('migration.list.filters.state')" :options="stateOptions" />
    </template>
  </UiFilterSheet>
</template>
