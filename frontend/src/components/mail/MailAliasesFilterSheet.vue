<script setup lang="ts">
/**
 * The aliases list's filter sheet: the account (administrators only), the domain and what the
 * alias is. The domain is picked by search and named in the URL by its name; the kind
 * choices come from the panel. Props in, events out: the page owns the applied filters in its URL.
 */
import { computed, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiCombobox from '../ui/UiCombobox.vue'
import UiFilterSheet from '../ui/UiFilterSheet.vue'
import UiSelect, { type SelectOption } from '../ui/UiSelect.vue'
import type { AccountPickerState } from '../../composables/useAccountChoice'
import type { MailDomainPickerState } from '../../composables/useMailDomainChoice'
import type { MailChoice } from '../../types/mailChoice'

/** Props accepted by {@link MailAliasesFilterSheet}. */
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
  /** The domain picker's state, whose options carry the domain's name as their value. */
  domainPicker: MailDomainPickerState
  /** The kinds of alias, already labelled in the reader's language. */
  kinds: readonly MailChoice[]
}>()

/** Events emitted by {@link MailAliasesFilterSheet}. */
const emit = defineEmits<{
  /** The reader applied the draft. */
  (e: 'apply', filters: Record<string, string>): void
  /** The reader closed the sheet. */
  (e: 'close'): void
  /** The account picker asked for accounts matching a search. */
  (e: 'search', query: string): void
  /** The domain picker asked for domains matching a search. */
  (e: 'searchDomains', query: string): void
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

/** The domain picker after an "any domain" choice, counted into the total. */
const domainFilterPicker: ComputedRef<MailDomainPickerState> = computed(() => {
  const state = props.domainPicker
  return {
    ...state,
    options: [{ value: '', label: t('common.list.any') }, ...state.options],
    total: state.total + 1,
  }
})

/** The kinds the filter offers, after an "any kind" choice. */
const kindOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    ...props.kinds.map((choice) => {
      return { value: choice.value, label: choice.label }
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

/**
 * Forwards a domain search.
 * @param query What the reader typed.
 * @returns Nothing; the event is emitted synchronously.
 */
const searchDomains = (query: string): void => {
  emit('searchDomains', query)
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
        :label="t('mail.aliases.filters.account')"
        :placeholder="t('common.list.any')"
        :selected-label="accountName"
        @search="search"
      />
      <UiCombobox
        v-model="draft.domain"
        v-bind="domainFilterPicker"
        :label="t('mail.aliases.filters.domain')"
        :placeholder="t('common.list.any')"
        :selected-label="draft.domain"
        @search="searchDomains"
      />
      <UiSelect v-model="draft.kind" :label="t('mail.aliases.filters.kind')" :options="kindOptions" />
    </template>
  </UiFilterSheet>
</template>
