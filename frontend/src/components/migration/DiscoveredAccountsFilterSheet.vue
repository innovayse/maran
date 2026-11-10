<script setup lang="ts">
/**
 * The discovered accounts list's filter sheet: which kind of content an account must have (sites,
 * databases, cron entries or mailboxes). The kinds are the closed set the panel's `has` filter
 * takes, named from the locale table. Props in, events out: the page owns the applied
 * filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { DiscoveredAccountHas } from '../../types/migrationSource'

/** Props accepted by {@link DiscoveredAccountsFilterSheet}. */
defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
}>()

/** Events emitted by {@link DiscoveredAccountsFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
}>()

/** The kinds of content an account can be filtered by. */
const KINDS: readonly DiscoveredAccountHas[] = ['sites', 'databases', 'cron', 'mail']

const { t } = useI18n()

/** The content choices, led by "any". */
const hasOptions: ComputedRef<SelectOption[]> = computed(() => {
  const named = KINDS.map((kind) => {
    return { value: kind, label: t(`migration.accounts.has.${kind}`) }
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
      <UiSelect v-model="draft.has" :label="t('migration.accounts.filters.has')" :options="hasOptions" />
    </template>
  </UiFilterSheet>
</template>
