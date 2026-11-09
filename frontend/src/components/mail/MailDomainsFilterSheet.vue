<script setup lang="ts">
/**
 * The mail domains list's filter sheet: the account (administrators only) and the domain's state.
 * The state choices come from the panel. Props in, events out: the page owns the applied filters
 * in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCombobox from '../ui/UiCombobox.vue'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import type { MailChoice } from '../../types/mailChoice'

/** Props accepted by {@link MailDomainsFilterSheet}. */
const props = defineProps<{
  /** Whether the sheet is open. */
  open: boolean
  /** The filters applied now, which the sheet's draft starts from. */
  filters: Record<string, string>
  /** Whether to offer the account filter; a customer's lists are already their own account's. */
  showAccount: boolean
  /** The account picker's state: the page of accounts matching its search. */
  accountPicker: AccountPickerState
  /** The chosen account's name, as the page reads it. */
  accountName: string
  /** The states a domain can be in, already labelled in the reader's language. */
  statuses: readonly MailChoice[]
}>()

/** Events emitted by {@link MailDomainsFilterSheet}. */
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

/** The states the filter offers, after an "any state" choice. */
const statusOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.statuses.map((status) => {
      return { value: status.value, label: status.label }
    }),
  ]
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
        v-if="showAccount"
        v-model="draft.account"
        v-bind="accountFilterPicker"
        :label="t('mail.domains.filters.account')"
        :placeholder="t('common.list.any')"
        :selected-label="accountName"
        @search="search"
      />
      <UiSelect v-model="draft.status" :label="t('mail.domains.filters.status')" :options="statusOptions" />
    </template>
  </UiFilterSheet>
</template>
