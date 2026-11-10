<script setup lang="ts">
/**
 * What an export is and how to take it to the other server: the package's facts, the key
 * fingerprint the receiving administrator compares and retypes, the statement that the package is
 * not encrypted, and the download. Props in, events out: the page owns the store.
 *
 * **The fingerprint is shown as text and copyable, and it is the point of the card.** The receiving
 * panel accepts a package only if its signature verifies against the fingerprint typed there, so the
 * administrator reads it here, on the sending server, and carries it by a channel of their own choice.
 */
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import CopyValueButton from './CopyValueButton.vue'
import { useLocaleStore } from '../../stores/locale'
import { formatBytes } from '../../utils/formatBytes'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { AccountExport, AccountExportAction } from '../../types/accountMove'

/** Props accepted by {@link ExportPackageCard}. */
defineProps<{
  /** The export to describe. */
  accountExport: AccountExport
  /** The action in flight, or `null`. */
  busy: AccountExportAction | null
}>()

/** Events emitted by {@link ExportPackageCard}. */
const emit = defineEmits<{
  /** The administrator asked to save the package. */
  (e: 'download'): void
}>()

const { t } = useI18n()
const localeStore = useLocaleStore()

/**
 * Asks the page to save the package.
 * @returns Nothing; emits synchronously.
 */
const download = (): void => {
  emit('download')
}
</script>

<template>
  <div class="flex min-w-0 flex-col gap-4">
    <UiSectionHeading :title="t('accountMove.export.package.title')" />
    <UiDescriptionList>
      <UiDescriptionItem :term="t('accountMove.export.package.account')" mono>{{ accountExport.accountName }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.package.size')">{{ formatBytes(accountExport.sizeBytes) }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.package.databases')" mono>{{ accountExport.databaseCount }}</UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.package.sections')" mono>
        {{ accountExport.sectionModules.join(', ') }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.package.createdAt')">
        {{ formatIsoTimestamp(accountExport.createdAt, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.package.expiresAt')">
        {{ formatIsoTimestamp(accountExport.expiresAt, localeStore.current) }}
      </UiDescriptionItem>
      <UiDescriptionItem :term="t('accountMove.export.package.sha256')" mono class="sm:col-span-2">
        {{ accountExport.sha256 }}
      </UiDescriptionItem>
    </UiDescriptionList>

    <UiSectionHeading :title="t('accountMove.export.fingerprint.title')" :subtitle="t('accountMove.export.fingerprint.intro')" />
    <!-- Selectable text, not a masked field: the clipboard may be unavailable. -->
    <code
      data-testid="export-fingerprint"
      class="block rounded-control border border-border-strong bg-surface-2 px-3 py-2.5 font-mono text-sm text-text-primary select-all wrap-anywhere"
      >{{ accountExport.keyFingerprint }}</code
    >
    <div>
      <CopyValueButton :value="accountExport.keyFingerprint" :label="t('accountMove.export.fingerprint.copy')" />
    </div>

    <UiAlert variant="warning">{{ t('accountMove.export.package.plaintext') }}</UiAlert>

    <div class="flex justify-end">
      <UiButton :disabled="busy !== null" @click="download">
        {{ busy === 'download' ? t('accountMove.export.package.downloading') : t('accountMove.export.package.download') }}
      </UiButton>
    </div>
  </div>
</template>
