<script setup lang="ts">
/**
 * The app store (issue #517): the web server and the runtimes sites run on. Two parts. The
 * installed apps are one server page at a time, searched and filtered by the panel
 * (`useServerTable`: page, search, sort and the kind and state filters live in the URL), with
 * start, stop, restart and uninstall in each row's actions menu. Below, what the host's package
 * sources can still install, with one install button each.
 *
 * Installs and removals are recorded tasks the apps store follows; their progress is drawn above
 * the table, and the lists are read again when one ends. A removal refused because sites still use
 * the app shows the panel's sentence and the list of those sites. Every refusal is the panel's own
 * text, printed verbatim.
 *
 * Renders a `<section>`, not a `<main>` (the layout owns the landmark). The page never touches the
 * API layer; the stores do.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiActiveFilter from '../../components/ui/UiActiveFilter.vue'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiDropdown from '../../components/ui/UiDropdown.vue'
import UiDropdownItem from '../../components/ui/UiDropdownItem.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiFilterSheet from '../../components/ui/UiFilterSheet.vue'
import UiFilterTrigger from '../../components/ui/UiFilterTrigger.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiPagination from '../../components/ui/UiPagination.vue'
import UiSearchInput from '../../components/ui/UiSearchInput.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSelect, { type SelectOption } from '../../components/ui/UiSelect.vue'
import UiTable from '../../components/ui/UiTable.vue'
import UiToolbar from '../../components/ui/UiToolbar.vue'
import AvailableAppsTable from '../../components/apps/AvailableAppsTable.vue'
import NodejsUnavailableAlert from '../../components/apps/NodejsUnavailableAlert.vue'
import HostAppInUseAlert from '../../components/apps/HostAppInUseAlert.vue'
import HostAppProgress from '../../components/apps/HostAppProgress.vue'
import HostAppServiceBadge from '../../components/apps/HostAppServiceBadge.vue'
import HostAppStateBadge from '../../components/apps/HostAppStateBadge.vue'
import { useListLabels } from '../../composables/useListLabels'
import { useServerTable } from '../../composables/useServerTable'
import { useLocaleStore } from '../../stores/locale'
import { useHostAppsStore } from '../../stores/hostApps'
import { useStackStore } from '../../stores/stack'
import type { HostApp, HostAppServiceAction } from '../../types/hostApp'
import type { ActiveFilterChip, ListLoad, ListRequest } from '../../types/list'
import type { UiTableColumn } from '../../types/tableColumn'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const store = useHostAppsStore()
const stackStore = useStackStore()
const localeStore = useLocaleStore()
const labels = useListLabels()

const table = useServerTable<HostApp>({
  filters: { kind: {}, state: {} },
  /**
   * Reads one page through the store.
   * @param request The page the URL describes.
   * @returns How the read ended.
   */
  load: (request: ListRequest): Promise<ListLoad<HostApp>> => {
    return store.list(request)
  },
})

/** Whether the filter sheet is open. */
const filtersOpen: Ref<boolean> = ref(false)

/** The app whose removal awaits the administrator's confirmation, or `null`. */
const pendingRemoval: Ref<HostApp | null> = ref(null)

/** Whether the confirmed removal is waiting for the panel's answer. */
const removing: Ref<boolean> = ref(false)

/** Whether the list is narrowed by a search or a filter, so an empty answer means "no match". */
const isNarrowed: ComputedRef<boolean> = computed(() => {
  return table.request.value.search !== '' || table.activeFilterCount.value > 0
})

/** Whether the latest answer arrived and holds no rows at all. */
const answeredNothing: ComputedRef<boolean> = computed(() => {
  return table.state.value.status === 'loaded' && table.total.value === 0
})

/** The kinds the filter offers; the option names are the panel's own chrome, as the statuses of other lists are. */
const kindOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    { value: 'webServer', label: t('apps.kind.webServer') },
    { value: 'runtime', label: t('apps.kind.runtime') },
  ]
})

/** The states the filter offers. */
const stateOptions: ComputedRef<SelectOption[]> = computed(() => {
  return [
    { value: '', label: t('common.list.any') },
    { value: 'installing', label: t('apps.state.installing') },
    { value: 'installed', label: t('apps.state.installed') },
    { value: 'failed', label: t('apps.state.failed') },
    { value: 'removing', label: t('apps.state.removing') },
  ]
})

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'name', label: t('apps.list.columns.name'), kind: 'identity', sortField: 'name' },
    { key: 'kind', label: t('apps.list.columns.kind'), kind: 'text', sortField: 'kind' },
    { key: 'version', label: t('apps.list.columns.version'), kind: 'mono' },
    { key: 'state', label: t('apps.list.columns.state'), kind: 'badge', sortField: 'state' },
    { key: 'service', label: t('apps.list.columns.service'), kind: 'badge' },
    { key: 'installedAt', label: t('apps.list.columns.installedAt'), kind: 'date', sortField: 'installedAt' },
    { key: 'actions', label: t('apps.list.columns.actions'), kind: 'actions' },
  ]
})

/** One removable chip per applied filter. */
const chips: ComputedRef<ActiveFilterChip[]> = computed(() => {
  const applied = table.filters.value
  const result: ActiveFilterChip[] = []
  if (applied.kind !== '') {
    result.push(labels.chip('kind', t('apps.list.filters.kind'), t(`apps.kind.${applied.kind}`)))
  }
  if (applied.state !== '') {
    result.push(labels.chip('state', t('apps.list.filters.state'), t(`apps.state.${applied.state}`)))
  }
  return result
})

/** The apps that can be installed and are not yet. */
const available = computed(() => {
  return store.installable.filter((app) => {
    return !app.installed
  })
})

/** The ids an install or removal is running for. */
const busyIds: ComputedRef<string[]> = computed(() => {
  return Object.keys(store.operations)
})

/** The followed operations to draw above the table: the app's name and the newest snapshot of its task. */
const operations = computed(() => {
  return Object.entries(store.operationTasks).map(([id, task]) => {
    const name =
      store.apps.find((app) => {
        return app.appId === id
      })?.name ??
      store.installable.find((app) => {
        return app.appId === id
      })?.name ??
      id
    return { id, name, task }
  })
})

/** The name of the app the last in-use refusal was about. */
const inUseName: ComputedRef<string> = computed(() => {
  const refusal = store.inUse
  if (refusal === null) {
    return ''
  }
  return (
    store.apps.find((app) => {
      return app.appId === refusal.appId
    })?.name ?? refusal.appId
  )
})

/** The question the removal dialog asks, naming the app. */
const removalName: ComputedRef<string> = computed(() => {
  return pendingRemoval.value?.name ?? ''
})

/**
 * Reads everything an install or removal can have changed: the installed page, the catalogue, and
 * the stack status the guard and the site form depend on.
 * @returns Resolves once the reads settled.
 */
const refresh = async (): Promise<void> => {
  await Promise.all([table.reload(), store.loadInstallable(), stackStore.load(true)])
}

/**
 * Installs an app and shows the row as installing.
 * @param id The catalogue id.
 * @returns Resolves once the panel answered and the lists were read again.
 */
const install = async (id: string): Promise<void> => {
  if (await store.install(id)) {
    await table.reload()
  }
}

/**
 * Asks for a service action on an app.
 * @param app The app.
 * @param action What the service should do.
 * @returns Resolves once the lists were read again.
 */
const control = async (app: HostApp, action: HostAppServiceAction): Promise<void> => {
  await store.control(app.appId, action)
  await table.reload()
}

/**
 * Opens the removal dialog for an app.
 * @param app The app to remove.
 * @returns Nothing.
 */
const askToRemove = (app: HostApp): void => {
  store.clearRefusals()
  pendingRemoval.value = app
}

/**
 * Abandons the pending removal.
 * @returns Nothing.
 */
const cancelRemoval = (): void => {
  pendingRemoval.value = null
}

/**
 * Starts the confirmed removal. The dialog closes once the panel answered (accepted or refused).
 * @returns Resolves once the lists were read again.
 */
const confirmRemoval = async (): Promise<void> => {
  const app = pendingRemoval.value
  if (app === null) {
    return
  }
  removing.value = true
  await store.uninstall(app.appId)
  removing.value = false
  pendingRemoval.value = null
  await table.reload()
}

/**
 * Opens the filter sheet.
 * @returns Nothing.
 */
const openFilters = (): void => {
  filtersOpen.value = true
}

/**
 * Closes the filter sheet.
 * @returns Nothing.
 */
const closeFilters = (): void => {
  filtersOpen.value = false
}

/**
 * Clears the search and every filter, the way back from a question that matched nothing.
 * @returns Resolves once the filters left the URL; the search follows after the box's pause.
 */
const clearAll = async (): Promise<void> => {
  table.search.value = ''
  await table.clearFilters()
}

// An operation ending changes what is installed, so the lists are read again when one stops being followed.
watch(
  () => {
    return busyIds.value.length
  },
  (now, before) => {
    if (now < before) {
      void refresh()
    }
  },
)

onMounted(store.loadInstallable)
onBeforeUnmount(() => {
  store.stopAll()
  store.clearRefusals()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading :title="t('apps.list.heading')" :subtitle="t('apps.list.subtitle')" />

    <UiAlert v-if="store.actionErrorMessage !== null" variant="error">{{ store.actionErrorMessage }}</UiAlert>
    <HostAppInUseAlert v-if="store.inUse !== null" :name="inUseName" :refusal="store.inUse" />

    <UiCard v-if="operations.length > 0">
      <div class="flex flex-col gap-3">
        <div v-for="operation in operations" :key="operation.id" :data-testid="`apps-operation-${operation.id}`">
          <p class="mb-1 text-sm font-semibold">{{ operation.name }}</p>
          <HostAppProgress :name="operation.name" :task="operation.task" />
        </div>
      </div>
    </UiCard>

    <UiCard>
      <div class="flex flex-col gap-4">
        <UiSectionHeading :title="t('apps.list.installedHeading')" />
        <UiToolbar>
          <UiSearchInput
            v-model="table.search.value"
            class="min-w-55 max-w-85 flex-1"
            :label="t('apps.list.searchLabel')"
            :placeholder="t('apps.list.searchPlaceholder')"
            :clear-label="t('apps.list.searchClear')"
          />
          <UiFilterTrigger
            :label="t('common.list.filters')"
            :active-count="table.activeFilterCount.value"
            :active-label="t('common.list.filtersApplied', { n: table.activeFilterCount.value }, table.activeFilterCount.value)"
            @click="openFilters"
          />
        </UiToolbar>

        <UiActiveFilter
          v-if="chips.length > 0"
          :chips="chips"
          :clear-label="t('common.list.clearAll')"
          @remove="table.clearFilter"
          @clear="table.clearFilters"
        />

        <UiLoadingRegion :state="table.loadingState.value">
          <template #error>
            <UiAlert v-if="table.state.value.status === 'refused'" variant="error">
              {{ table.state.value.message }}
            </UiAlert>
            <UiAlert v-else variant="error">{{ t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="answeredNothing && !isNarrowed"
            :title="t('apps.list.emptyTitle')"
            :description="t('apps.list.emptyDescription')"
          />
          <UiEmptyState
            v-else-if="answeredNothing"
            :title="t('apps.list.noResultsTitle')"
            :description="t('apps.list.noResultsDescription')"
          >
            <UiButton variant="secondary" @click="clearAll">{{ t('common.list.clearSearchAndFilters') }}</UiButton>
          </UiEmptyState>

          <div v-else class="flex flex-col gap-4">
            <UiTable
              :caption="t('apps.list.tableCaption')"
              :columns="columns"
              :rows="store.apps"
              row-key="appId"
              :loading="table.state.value.status === 'loading'"
              :skeleton-rows="table.pageSize.value"
              :sort="table.sort.value"
              @sort="table.toggleSort"
            >
              <template #cell-name="{ row }">{{ row.name }}</template>
              <template #cell-kind="{ row }">{{ row.kindName }}</template>
              <template #cell-version="{ row }">{{ row.version }}</template>
              <template #cell-state="{ row }"><HostAppStateBadge :state="row.state" :label="row.stateName" /></template>
              <template #cell-service="{ row }">
                <HostAppServiceBadge :status="row.serviceStatus" :label="row.serviceStatusName" />
              </template>
              <template #cell-installedAt="{ row }">{{ row.installedAt === null ? t('common.emptyValue') : formatDate(row.installedAt, localeStore.current) }}</template>
              <template #cell-actions="{ row }">
                <UiDropdown
                  :label="t('common.actions')"
                  :aria-label="t('apps.list.rowActions', { name: row.name })"
                  align="end"
                  variant="bare"
                  :chevron="false"
                  :disabled="row.state === 'installing' || row.state === 'removing' || busyIds.includes(row.appId)"
                >
                  <template #trigger>
                    <UiIcon name="ellipsis" size="md" />
                  </template>
                  <UiDropdownItem v-if="row.serviceStatus === 'inactive' || row.serviceStatus === 'failed'" @select="control(row, 'start')">
                    {{ t('apps.list.actions.start') }}
                  </UiDropdownItem>
                  <UiDropdownItem v-if="row.serviceStatus === 'active'" @select="control(row, 'stop')">
                    {{ t('apps.list.actions.stop') }}
                  </UiDropdownItem>
                  <UiDropdownItem v-if="row.serviceStatus !== 'none'" @select="control(row, 'restart')">
                    {{ t('apps.list.actions.restart') }}
                  </UiDropdownItem>
                  <UiDropdownItem destructive @select="askToRemove(row)">{{ t('apps.list.actions.uninstall') }}</UiDropdownItem>
                </UiDropdown>
              </template>
            </UiTable>

            <UiPagination
              v-if="table.state.value.status === 'loaded'"
              :page="table.page.value"
              :page-size="table.pageSize.value"
              :total="table.total.value"
              :page-sizes="table.pageSizes"
              :labels="labels.pagination.value"
              @update:page="table.setPage"
              @update:page-size="table.setPageSize"
            />
          </div>
        </UiLoadingRegion>
      </div>
    </UiCard>

    <UiCard>
      <div class="flex flex-col gap-4">
        <UiSectionHeading :title="t('apps.list.availableHeading')" :subtitle="t('apps.list.availableDescription')" />
        <UiLoadingRegion :state="store.installableState">
          <template #error>
            <UiAlert variant="error">{{ store.installableErrorMessage ?? t('apps.list.unreachable') }}</UiAlert>
          </template>
          <NodejsUnavailableAlert
            v-if="store.nodejsUnavailable !== null && store.nodejsUnavailableMessage !== null"
            :reason="store.nodejsUnavailable"
            :message="store.nodejsUnavailableMessage"
          />
          <AvailableAppsTable :apps="available" :busy="busyIds" @install="install" />
        </UiLoadingRegion>
      </div>
    </UiCard>

    <UiFilterSheet
      :open="filtersOpen"
      :model-value="table.filters.value"
      :title="t('common.list.filters')"
      :close-label="t('common.close')"
      :apply-label="t('common.list.apply')"
      :reset-label="t('common.list.reset')"
      @update:model-value="table.setFilters"
      @close="closeFilters"
    >
      <template #default="{ draft }">
        <UiSelect v-model="draft.kind" :label="t('apps.list.filters.kind')" :options="kindOptions" />
        <UiSelect v-model="draft.state" :label="t('apps.list.filters.state')" :options="stateOptions" />
      </template>
    </UiFilterSheet>

    <UiConfirm
      :open="pendingRemoval !== null"
      :title="t('apps.confirm.title', { name: removalName })"
      :question="t('apps.confirm.question', { name: removalName })"
      :acting="removing"
      :confirm-label="t('apps.confirm.confirm')"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting-label="t('apps.confirm.working')"
      @close="cancelRemoval"
      @confirm="confirmRemoval"
    />
  </section>
</template>
