<script setup lang="ts">
/**
 * The zones list's filter sheet: the owning account, which only an administrator is offered (a
 * customer's list is already their own account's, so the page does not open the sheet for them).
 * The accounts come from the picker's search. Props in, events out: the page owns the applied
 * filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCombobox from '../ui/UiCombobox.vue'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'

/** Props accepted by {@link DnsZonesFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
  /** The account picker's state: the page of accounts matching its search. */
  accountPicker: AccountPickerState
  /** The chosen account's name, as the page reads it. */
  accountName: string
}>()

/** Events emitted by {@link DnsZonesFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
  /** The account picker asked for accounts matching a search. */
  (e: 'search', query: string): void
}>()

const { t } = useI18n()

/** The account picker after an "any account" choice, counted into the total. */
const accountFilterPicker: ComputedRef<AccountPickerState> = computed(() => {
  const state = props.accountPicker
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
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

/**
 * Forwards an account search.
 * @param query What the reader typed.
 * @returns Nothing; the event is emitted synchronously.
 */
const search = (query: string): void => {
  emit('search', query)
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
      <UiCombobox
        v-model="draft.account"
        v-bind="accountFilterPicker"
        :label="t('dns.zones.filters.account')"
        :placeholder="t('common.list.any')"
        :selected-label="accountName"
        @search="search"
      />
    </template>
  </UiFilterSheet>
</template>
