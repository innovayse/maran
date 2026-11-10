<script setup lang="ts">
/**
 * The Updates tab: which installed modules have a newer version in the marketplace.
 *
 * **Nothing here is automatic.** The tab opens without a request; the only thing that asks the
 * marketplace is the "Check for updates" button (`store.checkUpdates`), so the panel never contacts
 * the marketplace on its own (rules/security.md item 10). Until the button is pressed the tab says
 * so, and it never claims "all up to date" without a check behind it. An answer that could not be
 * verified prints the panel's message and no verdict. Each row prints the panel's `canUpdate` and,
 * when it is false, its `cannotUpdateMessage`; a withdrawn installed version carries a warning with
 * the panel's reason. "Update" opens the same confirmation as an install, with the manifest summary,
 * in {@link ModuleOperationDialog}.
 */
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import MarketplaceFailureNotice from '../../components/marketplace/MarketplaceFailureNotice.vue'
import ModuleOperationDialog from '../../components/marketplace/ModuleOperationDialog.vue'
import { useLocaleStore } from '../../stores/locale'
import { useMarketplaceStore } from '../../stores/marketplace'
import type { ModuleOperationRequest, ModuleUpdate } from '../../types/marketplace'
import type { UiTableColumn } from '../../types/tableColumn'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'

const { t } = useI18n()
const store = useMarketplaceStore()
const localeStore = useLocaleStore()

/** The operation awaiting confirmation, or `null`. */
const request: Ref<ModuleOperationRequest | null> = ref(null)

/** The table's schema. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('marketplace.updates.name'), kind: 'identity', minWidth: '12rem' },
    { key: 'installedVersion', label: t('marketplace.updates.installedVersion'), kind: 'mono' },
    { key: 'availableVersion', label: t('marketplace.updates.availableVersion'), kind: 'mono' },
    { key: 'status', label: t('marketplace.updates.status'), kind: 'text', minWidth: '14rem' },
    { key: 'actions', label: t('common.actions'), kind: 'actions', align: 'end' },
  ]
})

/** When the check ran, formatted for the reader, or `null`. */
const checkedAt: ComputedRef<string | null> = computed(() => {
  const at = store.updates?.checkedAt ?? null
  return at === null ? null : formatIsoTimestamp(at, localeStore.current)
})

/** Whether the last answer was verified; false before any check, so no update can be started from nothing. */
const updatesVerified: ComputedRef<boolean> = computed(() => {
  return store.updates?.verified === true
})

/**
 * Whether an operation on a module is in flight (a request, or a task still being followed).
 * @param id The module's machine id.
 * @returns True while its Update button must stay off.
 */
const isBusy = (id: string): boolean => {
  return store.busy[id] === true || store.operations[id] !== undefined
}

/**
 * Asks the marketplace for newer versions. The only call this tab ever makes to it.
 * @returns Resolves once the check settled.
 */
const check = async (): Promise<void> => {
  await store.checkUpdates()
}

/**
 * Opens the update confirmation for a module.
 * @param update The row whose Update button was pressed.
 * @returns Nothing.
 */
const askUpdate = (update: ModuleUpdate): void => {
  request.value = { kind: 'update', id: update.id, name: update.displayName, version: update.availableVersion }
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <UiToolbar>
      <UiButton :disabled="store.updatesLoading" @click="check">
        {{ store.updatesLoading ? t('marketplace.updates.checking') : t('marketplace.updates.check') }}
      </UiButton>
      <p class="min-w-0 text-sm text-text-muted">
        {{
          checkedAt === null ? t('marketplace.updates.notChecked') : t('marketplace.updates.checkedAt', { time: checkedAt })
        }}
      </p>
    </UiToolbar>

    <MarketplaceFailureNotice
      :message="store.errorMessage"
      :unreachable="store.unreachable"
      :code="store.errorCode"
      @retry="check"
    />

    <template v-if="store.updates !== null">
      <UiAlert v-if="!store.updates.verified" variant="warning" data-testid="updates-unverified">
        <p class="font-medium">{{ t('marketplace.unverified.title') }}</p>
        <p v-if="store.updates.unverifiedMessage !== null">{{ store.updates.unverifiedMessage }}</p>
        <p>{{ t('marketplace.unverified.installsOff') }}</p>
      </UiAlert>
      <UiEmptyState
        v-else-if="store.updates.items.length === 0"
        :title="t('marketplace.updates.upToDateTitle')"
        :description="t('marketplace.updates.upToDateDescription')"
      />
      <UiTable
        v-if="store.updates.items.length > 0"
        :caption="t('marketplace.updates.listLabel')"
        :columns="columns"
        :rows="store.updates.items"
        row-key="id"
      >
        <template #cell-name="{ row }">{{ row.displayName }}</template>
        <template #cell-installedVersion="{ row }">{{ row.installedVersion }}</template>
        <template #cell-availableVersion="{ row }">{{ row.availableVersion ?? t('common.emptyValue') }}</template>
        <template #cell-status="{ row }">
          <p v-if="row.revoked" class="font-medium text-text-primary">{{ t('marketplace.installed.revoked') }}</p>
          <p v-if="row.revokedMessage !== null">{{ row.revokedMessage }}</p>
          <p v-if="row.cannotUpdateMessage !== null">{{ row.cannotUpdateMessage }}</p>
        </template>
        <template #cell-actions="{ row }">
          <UiButton
            v-if="row.canUpdate && row.availableVersion !== null"
            :disabled="isBusy(row.id) || !updatesVerified"
            :aria-label="t('marketplace.updates.updateFor', { name: row.displayName })"
            @click="askUpdate(row)"
          >
            {{ t('marketplace.card.update') }}
          </UiButton>
        </template>
      </UiTable>
    </template>

    <ModuleOperationDialog :request="request" @close="request = null" />
  </div>
</template>
