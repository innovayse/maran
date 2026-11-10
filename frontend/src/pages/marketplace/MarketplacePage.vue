<script setup lang="ts">
/**
 * The marketplace screen (issue #187): the switch card first, then, only while the marketplace is
 * switched on, the operations in flight and the Available, Installed and Updates tabs.
 *
 * **The switch gates everything.** On mount the page reads the switch (`store.loadStatus`) and
 * nothing else. With the switch off it renders the switch card alone, and the tabs, and therefore
 * every catalogue, installed-list and update request, do not exist: the panel contacts the
 * marketplace only on an administrator's act, after the administrator turned it on (rules/security.md
 * item 10). The tabs are a real tablist and only the chosen panel is mounted, so the Updates tab
 * stays silent until its own button is pressed.
 *
 * Operations the store follows (an install, update, removal or rollback, including one found again
 * after a reload) are drawn above the tabs by {@link ModuleOperationProgress}, wherever the
 * administrator started them; a failed one offers a retry that reopens the same confirmation.
 * Renders a `<section>`, not a `<main>` (the layout owns the landmark). The page loads in
 * `onMounted` because the layout remounts it when the language changes, which re-reads the
 * panel's localized text.
 */
import { computed, onBeforeUnmount, onMounted, ref, watch, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiTabs, { type TabOption } from '../../components/ui/UiTabs.vue'
import MarketplaceSwitchCard from '../../components/marketplace/MarketplaceSwitchCard.vue'
import ModuleOperationDialog from '../../components/marketplace/ModuleOperationDialog.vue'
import ModuleOperationProgress from '../../components/marketplace/ModuleOperationProgress.vue'
import { useMarketplaceStore } from '../../stores/marketplace'
import type { LoadingState } from '../../types/loading'
import { moduleOperationKindOf } from '../../utils/moduleOperationKindOf'
import AvailableModulesTab from './AvailableModulesTab.vue'
import InstalledModulesTab from './InstalledModulesTab.vue'
import UpdatesTab from './UpdatesTab.vue'
import type { ModuleOperationRequest } from '../../types/marketplace'

const { t } = useI18n()
const store = useMarketplaceStore()

/** The tab on screen. */
const tab: Ref<string> = ref('available')

/** The operation a failed task's retry asked to confirm, or `null`. */
const request: Ref<ModuleOperationRequest | null> = ref(null)

/** The tabs, in reading order. */
const tabs: ComputedRef<TabOption[]> = computed(() => {
  return [
    { id: 'available', label: t('marketplace.tabs.available') },
    { id: 'installed', label: t('marketplace.tabs.installed') },
    { id: 'updates', label: t('marketplace.tabs.updates') },
  ]
})

/** The loading region's state for the switch read: error only once a read failed with nothing held. */
const loadState: ComputedRef<LoadingState> = computed(() => {
  if (store.status !== null) {
    return 'loaded'
  }
  return !store.statusLoading && (store.errorMessage !== null || store.unreachable) ? 'error' : 'loading'
})

/** Ids of the modules with an operation followed or an outcome still on screen. */
const operationIds: ComputedRef<string[]> = computed(() => {
  return [...new Set([...Object.keys(store.operationTasks), ...Object.keys(store.operations)])]
})

/**
 * The name to print for a module id, from whatever the store holds; the id itself when nothing names it.
 * @param id The module's machine id.
 * @returns The module's name as the panel localized it, else the id.
 */
const nameOf = (id: string): string => {
  const known =
    store.details[id]?.module.displayName ??
    store.items.find((item) => {
      return item.id === id
    })?.displayName ??
    store.installed?.find((item) => {
      return item.id === id
    })?.displayName
  return known ?? id
}

/**
 * Reopens the confirmation of a failed operation.
 * @param id The module's machine id.
 * @returns Nothing.
 */
const retry = (id: string): void => {
  const kind = moduleOperationKindOf(store.operationTasks[id]?.kind ?? '')
  if (kind !== undefined) {
    request.value = { kind, id, name: nameOf(id), version: null }
  }
}

watch(
  (): boolean => {
    return store.enabled
  },
  (on: boolean): void => {
    // Switched on from this screen: tasks started earlier are found again, as after a reload.
    if (on) {
      void store.resumeOperations()
    }
  },
)

onMounted(() => {
  void store.loadStatus()
})

onBeforeUnmount(() => {
  store.stopFollowing()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <UiPageHeading :title="t('marketplace.title')" :subtitle="t('marketplace.subtitle')" />

    <UiLoadingRegion :state="loadState">
      <template #error>
        <UiAlert variant="error">{{ store.errorMessage ?? t('marketplace.unreachable.description') }}</UiAlert>
      </template>
      <div v-if="store.status !== null" class="flex flex-col gap-4">
        <MarketplaceSwitchCard :status="store.status" />

        <template v-if="store.enabled">
          <ModuleOperationProgress
            v-for="id in operationIds"
            :key="id"
            :module-id="id"
            :module-name="nameOf(id)"
            :task="store.operationTasks[id] ?? null"
            :following="store.operations[id] !== undefined"
            @retry="retry(id)"
            @dismiss="store.dismissOutcome(id)"
          />

          <UiTabs v-model="tab" :tabs="tabs" :label="t('marketplace.tabs.label')">
            <template #available><AvailableModulesTab /></template>
            <template #installed><InstalledModulesTab /></template>
            <template #updates><UpdatesTab /></template>
          </UiTabs>
        </template>
      </div>
    </UiLoadingRegion>

    <ModuleOperationDialog :request="request" @close="request = null" />
  </section>
</template>
