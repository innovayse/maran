<script setup lang="ts">
/**
 * One discovered account in a dialog: where it lives, how much disk it uses, the sites it serves
 * (domains, aliases, how each is served, PHP version, document root) and the databases it owns.
 * Everything here is what discovery READ from the remote server; the names, paths and domains are
 * source data and are shown as received, never interpreted. Mail is counted but not migrated, and
 * the dialog says so beside the figure.
 *
 * Props in, events out: the tab owns the store and the open/closed state.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiModal from '../ui/UiModal.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiSpinner from '../ui/UiSpinner.vue'
import { formatBytes } from '../../utils/formatBytes'
import type { LoadingState } from '../../types/loading'
import type { DiscoveredAccountDetail } from '../../types/migrationSource'

/** Props accepted by {@link DiscoveredAccountDetail}. */
defineProps<{
  /** Whether the dialog is open. */
  open: boolean
  /** The account's login name, for the dialog's title while the detail is still on its way. */
  name: string
  /** Where the read of the account stands. */
  state: LoadingState
  /** The account once it arrived, or `null`. */
  account: DiscoveredAccountDetail | null
  /** The panel's reason the read failed, already localized, or `null`. */
  errorMessage: string | null
}>()

/** Events emitted by {@link DiscoveredAccountDetail}. */
const emit = defineEmits<{
  /** The reader closed the dialog. */
  (e: 'close'): void
}>()

const { t } = useI18n()

/**
 * Forwards a close.
 * @returns Nothing; the event is emitted synchronously.
 */
const close = (): void => {
  emit('close')
}
</script>

<template>
  <UiModal :open="open" :title="t('migration.account.title', { name })" :close-label="t('common.close')" @close="close">
    <div v-if="state === 'loading'" class="flex justify-center py-6">
      <UiSpinner :label="t('common.loading.busy')" />
    </div>

    <UiAlert v-else-if="state === 'error' || account === null" variant="error">
      {{ errorMessage ?? t('migration.account.loadFailed') }}
    </UiAlert>

    <div v-else class="flex min-w-0 flex-col gap-5">
      <UiDescriptionList>
        <UiDescriptionItem :term="t('migration.account.home')" mono class="sm:col-span-2">{{ account.home }}</UiDescriptionItem>
        <UiDescriptionItem :term="t('migration.account.disk')">
          {{ account.diskKnown ? formatBytes(account.diskBytes) : t('migration.accounts.diskUnknown') }}
        </UiDescriptionItem>
        <UiDescriptionItem :term="t('migration.account.cron')">{{ account.cronEntryCount }}</UiDescriptionItem>
        <UiDescriptionItem :term="t('migration.account.mailboxes')">
          {{ account.mailboxCount }}
          <span v-if="account.mailboxCount > 0" class="text-text-muted"> — {{ t('migration.account.mailNotMigrated') }}</span>
        </UiDescriptionItem>
      </UiDescriptionList>

      <UiSectionHeading :title="t('migration.account.sitesTitle')" />
      <p v-if="account.sites.length === 0" class="text-sm text-text-muted">{{ t('migration.account.noSites') }}</p>
      <ul v-else class="flex flex-col gap-3">
        <li v-for="site in account.sites" :key="site.domain" class="rounded-control border border-border-subtle p-3">
          <p class="font-mono text-sm font-semibold text-text-primary wrap-anywhere">{{ site.domain }}</p>
          <UiDescriptionList class="mt-2">
            <UiDescriptionItem v-if="site.aliases.length > 0" :term="t('migration.account.aliases')" mono class="sm:col-span-2">
              {{ site.aliases.join(' ') }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('migration.account.backend')">
              {{ t(`migration.account.backends.${site.backend}`) }}
            </UiDescriptionItem>
            <UiDescriptionItem v-if="site.phpVersion" :term="t('migration.account.php')" mono>
              {{ site.phpVersion }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('migration.account.documentRoot')" mono class="sm:col-span-2">
              {{ site.documentRoot }}
            </UiDescriptionItem>
          </UiDescriptionList>
        </li>
      </ul>

      <UiSectionHeading :title="t('migration.account.databasesTitle')" />
      <p v-if="account.databases.length === 0" class="text-sm text-text-muted">{{ t('migration.account.noDatabases') }}</p>
      <ul v-else class="flex flex-col gap-2">
        <li
          v-for="database in account.databases"
          :key="database.name"
          class="flex items-center justify-between gap-3 rounded-control border border-border-subtle px-3 py-2"
        >
          <span class="min-w-0 font-mono text-sm text-text-primary wrap-anywhere">{{ database.name }}</span>
          <span class="shrink-0 text-sm text-text-muted">
            {{ t('migration.account.databaseSize') }}:
            {{ database.sizeKnown ? formatBytes(database.sizeBytes) : t('migration.account.sizeUnknown') }}
          </span>
        </li>
      </ul>
    </div>
  </UiModal>
</template>
