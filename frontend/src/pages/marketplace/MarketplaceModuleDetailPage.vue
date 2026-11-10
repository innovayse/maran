<script setup lang="ts">
/**
 * One marketplace module's screen (issue #187): its summary and licence standing, what it declares
 * it can reach, every published version with its release notes, and the install action.
 *
 * The page's title is the module's name, reported to the breadcrumb through `UiPageHeading` (the
 * route is `titledByRecord`). Like the list, it reads the marketplace only while the switch is on:
 * with the switch off it prints that and makes no request. Every verdict is the panel's: the Install
 * buttons follow `canInstall` and each version's `installable`, a refusal is printed as the panel's
 * text, and release notes are plain text. Install and update open {@link ModuleOperationDialog};
 * progress of an operation on this module is drawn here too. Renders a `<section>`; loads in
 * `onMounted` because the layout remounts the page when the language changes.
 */
import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import MarketplaceFailureNotice from '../../components/marketplace/MarketplaceFailureNotice.vue'
import ModuleManifestList from '../../components/marketplace/ModuleManifestList.vue'
import ModuleOperationDialog from '../../components/marketplace/ModuleOperationDialog.vue'
import ModuleOperationProgress from '../../components/marketplace/ModuleOperationProgress.vue'
import ModuleSummary from '../../components/marketplace/ModuleSummary.vue'
import ModuleVersionList from '../../components/marketplace/ModuleVersionList.vue'
import { useMarketplaceStore } from '../../stores/marketplace'
import type { LoadingState } from '../../types/loading'
import type { MarketplaceModuleDetail, ModuleOperationRequest } from '../../types/marketplace'
import { moduleOperationKindOf } from '../../utils/moduleOperationKindOf'

const { t } = useI18n()
const route = useRoute()
const store = useMarketplaceStore()

/** The operation awaiting confirmation, or `null`. */
const request: Ref<ModuleOperationRequest | null> = ref(null)

/** The module's id from the address. */
const id: ComputedRef<string> = computed(() => {
  const raw = route.params.id
  return typeof raw === 'string' ? raw : (raw?.[0] ?? '')
})

/** The module as the store holds it, or `undefined` until read. */
const detail: ComputedRef<MarketplaceModuleDetail | undefined> = computed(() => {
  return store.details[id.value]
})

/** Whether the switch is known to be off, in which case there is nothing to read. */
const switchedOff: ComputedRef<boolean> = computed(() => {
  return store.status !== null && !store.enabled
})

/** The loading region's state: loaded once the module arrived; an error for a switched-off marketplace or a failed read. */
const loadState: ComputedRef<LoadingState> = computed(() => {
  if (detail.value !== undefined) {
    return 'loaded'
  }
  if (switchedOff.value) {
    return 'error'
  }
  const refused = store.errorMessage !== null || store.unreachable
  return refused && !store.detailsLoading && !store.statusLoading ? 'error' : 'loading'
})

/** Whether an operation on this module is in flight or its outcome is still on screen. */
const hasOperation: ComputedRef<boolean> = computed(() => {
  return store.operationTasks[id.value] !== undefined || store.operations[id.value] !== undefined
})

/** Whether a request or an operation on this module is running, which turns every action off. */
const busy: ComputedRef<boolean> = computed(() => {
  return store.busy[id.value] === true || store.operations[id.value] !== undefined
})

/**
 * Opens the confirmation to install (or, for an installed module, update to) a version.
 * @param version The version, or `null` for the newest the catalogue offers.
 * @returns Nothing.
 */
const ask = (version: string | null): void => {
  const module = detail.value?.module
  if (module !== undefined) {
    request.value = {
      kind: module.installedVersion === null ? 'install' : 'update',
      id: module.id,
      name: module.displayName,
      version,
    }
  }
}

/**
 * Opens the confirmation for the newest version the catalogue offers.
 * @returns Nothing.
 */
const askLatest = (): void => {
  ask(null)
}

/**
 * Reopens the confirmation of a failed operation on this module, whichever operation it was.
 * @returns Nothing.
 */
const retry = (): void => {
  const module = detail.value?.module
  const kind = moduleOperationKindOf(store.operationTasks[id.value]?.kind ?? '')
  if (module !== undefined && kind !== undefined) {
    request.value = { kind, id: module.id, name: module.displayName, version: null }
  }
}

/**
 * Reads the module, after the switch is known.
 * @returns Resolves once the reads settled.
 */
const load = async (): Promise<void> => {
  if (store.status === null) {
    await store.loadStatus()
  }
  await store.loadDetails(id.value)
}

onMounted(() => {
  void load()
})

onBeforeUnmount(() => {
  store.stopFollowing()
})
</script>

<template>
  <section class="flex w-full flex-col gap-4">
    <div>
      <UiLink :to="{ name: 'marketplace' }" icon="chevronLeft">{{ t('marketplace.detail.back') }}</UiLink>
    </div>
    <UiPageHeading
      :title="detail?.module.displayName ?? t('marketplace.detail.title')"
      :subtitle="detail?.module.summary"
      :loading="loadState === 'loading'"
    />

    <UiLoadingRegion :state="loadState">
      <template #error>
        <UiEmptyState
          v-if="switchedOff"
          :title="t('marketplace.detail.offTitle')"
          :description="t('marketplace.detail.offDescription')"
        />
        <MarketplaceFailureNotice
          v-else
          :message="store.errorMessage"
          :unreachable="store.unreachable"
          :code="store.errorCode"
          @retry="load"
        />
      </template>

      <div v-if="detail !== undefined" class="flex flex-col gap-4">
        <ModuleOperationProgress
          v-if="hasOperation"
          :module-id="detail.module.id"
          :module-name="detail.module.displayName"
          :task="store.operationTasks[detail.module.id] ?? null"
          :following="store.operations[detail.module.id] !== undefined"
          @retry="retry"
          @dismiss="store.dismissOutcome(id)"
        />

        <ModuleSummary :module="detail.module" :busy="busy" @install="askLatest" />

        <UiCard>
          <div class="flex flex-col gap-3">
            <UiSectionHeading :title="t('marketplace.manifest.title')" />
            <ModuleManifestList v-if="detail.manifest !== null" :manifest="detail.manifest" />
            <p v-else class="text-sm text-text-muted">{{ t('marketplace.install.manifestMissing') }}</p>
          </div>
        </UiCard>

        <UiCard>
          <div class="flex flex-col gap-3">
            <UiSectionHeading :title="t('marketplace.versions.title')" />
            <ModuleVersionList
              :versions="detail.versions"
              :disabled="busy"
              @install="ask"
            />
          </div>
        </UiCard>
      </div>
    </UiLoadingRegion>

    <ModuleOperationDialog :request="request" @close="request = null" />
  </section>
</template>
