<script setup lang="ts">
/**
 * What an import is: the account and the server it comes from, the key that signed the package, the
 * sizes and the modules that contributed. Props in, nothing out: it is a description.
 */
import { useI18n } from 'vue-i18n'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import AccountImportStateBadge from './AccountImportStateBadge.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AccountImport } from '../../types/accountMove'

/** Props accepted by {@link AccountImportSummary}. */
defineProps<{
  /** The import to describe. */
  accountImport: AccountImport
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('accountMove.import.summary.title')" />
    <UiDescriptionList>
      <UiDescriptionItem :term="t('accountMove.import.summary.state')">
        <AccountImportStateBadge :state="accountImport.state" :name="accountImport.stateName" />
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.account')" mono>{{ accountImport.accountName }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.domain')" mono>{{ accountImport.primaryDomain }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.sourceHost')" mono>{{ accountImport.sourceHostName }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.size')">{{ formatBytes(accountImport.sizeBytes) }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.home')">{{ formatBytes(accountImport.homeBytes) }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.databases')" mono>{{ accountImport.databaseCount }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.sections')" mono>
        {{ accountImport.sectionModules.join(', ') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.acceptedAt')">
        {{ formatIsoTimestamp(accountImport.createdAt, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem v-if="accountImport.finishedAt !== null" :term="t('accountMove.import.summary.finishedAt')">
        {{ formatIsoTimestamp(accountImport.finishedAt, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.import.summary.fingerprint')" mono class="sm:col-span-2">
        {{ accountImport.keyFingerprint }}
      </UiDescriptionItem>
    </UiDescriptionList>
  </div>
</template>
