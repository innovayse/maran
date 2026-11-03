<script setup lang="ts">
/**
 * The facts of one hosting account, as a description list: its name, primary domain, status, plan,
 * owner's address and creation date.
 *
 * Dumb: the account page passes the account and whether it is still on its way. While it loads, the
 * card is drawn already with its real terms and a placeholder line in place of each value
 * (`UiDescriptionList loading`), so the account lands in the space held for it.
 */
import { useI18n } from 'vue-i18n'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import AccountStatusBadge from './AccountStatusBadge.vue'
import type { AccountDetail } from '../../types/account'
import type { AppLocale } from '../../types/app'
import { formatDate } from '../../utils/formatDate'

/** Props accepted by {@link AccountDetailsCard}. */
defineProps<{
  /** The account to describe; `null` is the one state in which it is still on its way. */
  account: AccountDetail | null
  /** The interface language the creation date is written in. */
  locale: AppLocale
}>()

const { t } = useI18n()
</script>

<template>
  <UiCard>
    <!-- The values are read only once loaded: while loading each item draws a placeholder
         line in place of its slot. -->
    <UiDescriptionList :loading="account === null">
      <UiDescriptionItem :term="t('common.name')" mono>
        {{ account?.name }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accounts.detail.primaryDomainLabel')" mono>
        {{ account?.primaryDomain }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accounts.detail.statusLabel')">
        <AccountStatusBadge v-if="account !== null" :status="account.status" :label="account.statusName" />
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accounts.detail.planLabel')">
        {{ account?.planDisplayName }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accounts.detail.ownerEmailLabel')" mono>
        {{ account?.ownerEmail ?? t('accounts.detail.ownerEmailUnknown') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accounts.detail.createdAtLabel')" mono>
        {{ account === null ? '' : formatDate(account.createdAt, locale) }}
      </UiDescriptionItem>
    </UiDescriptionList>
  </UiCard>
</template>
