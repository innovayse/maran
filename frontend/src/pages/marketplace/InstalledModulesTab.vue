<script setup lang="ts">
/**
 * The Installed tab: the modules on this panel, their lifecycle and the actions the panel allows.
 *
 * Each row shows the version, a lifecycle badge (restart required, running, degraded, failed,
 * removed), where the module came from and when it was installed. A degraded or failed module prints
 * the panel's `failureMessage`; a removed one says its data is kept; a withdrawn version says so.
 * Rollback and Remove are offered only when the panel says `canRollback` / `canRemove`, and are off
 * while an operation on that module runs; both ask for the password in
 * {@link ModuleOperationDialog}. A module installed but not yet started shows
 * {@link RestartRequiredNotice}: confirming it restarts the panel through the store and then waits
 * for the module by polling the LOCAL panel, printing how the wait ended.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge, { type BadgeVariant } from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiTable from '../../components/ui/UiTable.vue'
import MarketplaceFailureNotice from '../../components/marketplace/MarketplaceFailureNotice.vue'
import ModuleOperationDialog from '../../components/marketplace/ModuleOperationDialog.vue'
import RestartRequiredNotice from '../../components/marketplace/RestartRequiredNotice.vue'
import { useLocaleStore } from '../../stores/locale'
import { useMarketplaceStore } from '../../stores/marketplace'
import type { LoadingState } from '../../types/loading'
import type {
  InstalledModule,
  InstalledModuleLifecycle,
  ModuleOperationRequest,
  ModuleWaitOutcome,
} from '../../types/marketplace'
import type { UiTableColumn } from '../../types/tableColumn'
import { formatDate } from '../../utils/formatDate'

/** How a restart wait ended for one module, kept to print after the module left "restart required". */
interface RestartOutcome {
  /** The module's machine id. */
  id: string
  /** The module's name, localized by the panel. */
  name: string
  /** How the wait ended. */
  outcome: ModuleWaitOutcome
}

/** The tone each lifecycle is drawn in. */
const LIFECYCLE_TONES: Record<InstalledModuleLifecycle, BadgeVariant> = {
  installed: 'warning',
  active: 'success',
  degraded: 'warning',
  failed: 'danger',
  removed: 'neutral',
}

const { t } = useI18n()
const store = useMarketplaceStore()
const localeStore = useLocaleStore()

/** The operation awaiting confirmation, or `null`. */
const request: Ref<ModuleOperationRequest | null> = ref(null)

/** The id of the module whose restart is being waited for, or `null`. */
const restartingId: Ref<string | null> = ref(null)

/** How the restart waits that ended this visit ended. */
const outcomes: Ref<RestartOutcome[]> = ref([])

/** The table's schema. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('marketplace.installed.name'), kind: 'identity', minWidth: '14rem' },
    { key: 'version', label: t('marketplace.installed.version'), kind: 'mono' },
    { key: 'state', label: t('marketplace.installed.state'), kind: 'badge' },
    { key: 'origin', label: t('marketplace.installed.origin'), kind: 'text', minWidth: '10rem' },
    { key: 'installedAt', label: t('marketplace.installed.installedAt'), kind: 'date' },
    { key: 'actions', label: t('common.actions'), kind: 'actions', align: 'end' },
  ]
})

/** The loading region's state: placeholders only until the first list arrived. */
const loadState: ComputedRef<LoadingState> = computed(() => {
  return store.installedLoading && store.installed === null ? 'loading' : 'loaded'
})

/** The modules staged and waiting for a restart. */
const waiting: ComputedRef<InstalledModule[]> = computed(() => {
  return (store.installed ?? []).filter((module) => {
    return module.restartRequired
  })
})

/**
 * Whether an operation on a module is in flight (a request, or a task still being followed).
 * @param id The module's machine id.
 * @returns True while its actions must stay off.
 */
const isBusy = (id: string): boolean => {
  return store.busy[id] === true || store.operations[id] !== undefined
}

/**
 * Opens the confirmation for a removal or a rollback.
 * @param kind Which of the two.
 * @param module The module the row belongs to.
 * @returns Nothing.
 */
const ask = (kind: 'remove' | 'rollback', module: InstalledModule): void => {
  request.value = { kind, id: module.id, name: module.displayName, version: null }
}

/**
 * Restarts the panel for a staged module, then waits for the module to start and records how the wait ended.
 * @param module The module waiting for the restart.
 * @returns Resolves once the wait ended or the restart was refused.
 */
const restart = async (module: InstalledModule): Promise<void> => {
  if (!(await store.restart(module.id))) {
    return
  }
  restartingId.value = module.id
  const outcome = await store.waitForModule(module.id, module.version)
  restartingId.value = null
  if (outcome !== 'cancelled') {
    outcomes.value = [...outcomes.value, { id: module.id, name: module.displayName, outcome }]
  }
}

/**
 * Reads the list again, as the retry of a failed read.
 * @returns Resolves once the read settled.
 */
const retry = async (): Promise<void> => {
  await store.loadInstalled()
}

onMounted(() => {
  void store.loadInstalled()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <MarketplaceFailureNotice
      :message="store.errorMessage"
      :unreachable="store.unreachable"
      :code="store.errorCode"
      @retry="retry"
    />

    <RestartRequiredNotice
      v-for="module in waiting"
      :key="module.id"
      :module-name="module.displayName"
      :busy="store.busy[module.id] === true"
      :restarting="restartingId === module.id && store.restarting"
      @restart="restart(module)"
    />

    <template v-for="item in outcomes" :key="item.id">
      <UiAlert v-if="item.outcome === 'changed'" variant="success" data-testid="restart-ready">
        {{ t('marketplace.restart.ready', { name: item.name }) }}
      </UiAlert>
      <UiAlert v-else variant="warning" data-testid="restart-timeout">
        <p class="font-medium">{{ t('marketplace.restart.timeoutTitle') }}</p>
        <p>{{ t('marketplace.restart.timeoutDescription') }}</p>
      </UiAlert>
    </template>

    <UiLoadingRegion :state="loadState">
      <UiTable
        v-if="loadState === 'loading' || (store.installed !== null && store.installed.length > 0)"
        :caption="t('marketplace.installed.listLabel')"
        :columns="columns"
        :rows="store.installed ?? []"
        :loading="loadState === 'loading'"
        :skeleton-rows="3"
        row-key="id"
      >
        <template #cell-name="{ row }">
          <p>{{ row.displayName }}</p>
          <p v-if="row.failureMessage !== null" class="mt-1 text-sm text-text-secondary wrap-anywhere">
            {{ row.failureMessage }}
          </p>
          <p v-if="row.dataKept" class="mt-1 text-sm text-text-secondary" data-testid="module-data-kept">
            {{ t('marketplace.installed.dataKept') }}
          </p>
          <p v-if="row.revoked" class="mt-1 text-sm text-text-secondary">{{ t('marketplace.installed.revoked') }}</p>
          <p v-if="row.previousVersion !== null" class="mt-1 text-sm text-text-muted">
            {{ t('marketplace.installed.previousVersionValue', { version: row.previousVersion }) }}
          </p>
        </template>
        <template #cell-version="{ row }">{{ row.version }}</template>
        <template #cell-state="{ row }">
          <UiBadge :variant="LIFECYCLE_TONES[row.lifecycle]">{{ t(`marketplace.lifecycle.${row.lifecycle}`) }}</UiBadge>
        </template>
        <template #cell-origin="{ row }">{{ row.origin }}</template>
        <template #cell-installedAt="{ row }">
          {{ row.installedAt === null ? t('common.emptyValue') : formatDate(row.installedAt, localeStore.current) }}
        </template>
        <template #cell-actions="{ row }">
          <div class="flex flex-wrap justify-end gap-2">
            <UiButton
              v-if="row.canRollback"
              variant="secondary"
              :disabled="isBusy(row.id)"
              :aria-label="t('marketplace.installed.rollbackFor', { name: row.displayName })"
              @click="ask('rollback', row)"
            >
              {{ t('marketplace.installed.rollback') }}
            </UiButton>
            <UiButton
              v-if="row.canRemove"
              variant="destructive"
              :disabled="isBusy(row.id)"
              :aria-label="t('marketplace.installed.removeFor', { name: row.displayName })"
              @click="ask('remove', row)"
            >
              {{ t('marketplace.installed.remove') }}
            </UiButton>
          </div>
        </template>
      </UiTable>
      <UiEmptyState
        v-else-if="store.installed !== null"
        :title="t('marketplace.installed.emptyTitle')"
        :description="t('marketplace.installed.emptyDescription')"
      />
    </UiLoadingRegion>

    <ModuleOperationDialog :request="request" @close="request = null" />
  </div>
</template>
