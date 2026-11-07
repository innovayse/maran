<script setup lang="ts">
/**
 * The personal-data export of one hosting account, on the account's own page: one button that
 * downloads everything the panel holds about the account and its team as a JSON file. What is in
 * the file, what is left out (secrets, customer files) and the rule that records each export in
 * the audit journal are the panel's; this card only asks and saves.
 *
 * The panel's refusal (a missing account, an export too large to hand over, an exhausted
 * allowance) is shown as it came. Because the store keeps the state of the LAST export wherever it
 * was made, the card shows an outcome only after a click on THIS account's card: opening another
 * account never shows the previous one's refusal.
 *
 * Administrator-only on the server (`PrivacyController`); the page draws this card for an
 * administrator only, and the endpoint is the boundary (rules/architecture.md).
 */
import { ref, watch, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import { usePrivacyStore } from '../../stores/privacy'

/** Props accepted by {@link AccountPersonalDataExport}. */
const props = defineProps<{
  /** The hosting account whose data is exported. */
  accountId: string
}>()

const { t } = useI18n()
const store = usePrivacyStore()

/** Whether an export was started from this card for this account, so the store's outcome is this card's. */
const attempted: Ref<boolean> = ref(false)

/**
 * Asks the panel for the account's data and saves the file it sends.
 * @returns Resolves once the download settled, successfully or not.
 */
const exportData = async (): Promise<void> => {
  attempted.value = true
  await store.exportAccount(props.accountId)
}

// Another account's page reuses this card: its outcome starts blank.
watch(
  (): string => {
    return props.accountId
  },
  (): void => {
    attempted.value = false
  },
)
</script>

<template>
  <UiCard class="flex flex-col gap-4">
    <UiSectionHeading
      :title="t('accounts.detail.personalDataExport')"
      :subtitle="t('accounts.detail.personalDataExportDescription')"
    />

    <UiAlert v-if="attempted && store.exportState.state === 'failed'" variant="error">
      {{ store.exportMessage ?? t('common.list.unreachable') }}
    </UiAlert>
    <UiAlert v-else-if="attempted && store.exportState.state === 'done'" variant="success">
      {{ t('common.done') }}
    </UiAlert>

    <div>
      <UiButton variant="secondary" :disabled="store.exporting" @click="exportData">
        {{ store.exporting ? t('common.loading.busy') : t('accounts.detail.personalDataExport') }}
      </UiButton>
    </div>
  </UiCard>
</template>
