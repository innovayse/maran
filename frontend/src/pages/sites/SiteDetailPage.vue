<script setup lang="ts">
/**
 * One site, in three tabs: what it is, what it is logging, and what certifies it.
 *
 * Every action asks first, in {@link UiConfirm}, and the question names the consequence rather
 * than asking the operator to repeat themselves. Deletion in particular says exactly what the contract does:
 * the vhost is removed and the domain stops being served, and the files in the document root
 * are left on disk. An operator who believes deletion wipes the customer's data will hesitate
 * over a harmless action; one who believes it does not, when it does, will not hesitate over a
 * destructive one. Both are the screen's fault.
 *
 * The overview lays the details in one card and, beside it (below it on a phone), the PHP rebind
 * card and the lifecycle actions. The Logs and SSL panes are separate components rendered as-is.
 *
 * While the site loads, the heading and the details card are drawn already, with their real terms
 * and a placeholder line in place of each value ({@link UiLoadingRegion},
 * `UiDescriptionList loading`), so the site lands in the space held for it.
 *
 * Renders a `<section>`, not a `<main>` — the layout owns the landmark.
 */
import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import type { LoadingState } from '../../types/loading'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSkeleton from '../../components/ui/UiSkeleton.vue'
import PhpVersionSelect from '../../components/sites/PhpVersionSelect.vue'
import SiteStatusBadge from '../../components/sites/SiteStatusBadge.vue'
import SiteLogsTab from './SiteLogsTab.vue'
import SiteSslTab from './SiteSslTab.vue'
import { useLocaleStore } from '../../stores/locale'
import { useSitesStore } from '../../stores/sites'
import { formatDate } from '../../utils/formatDate'
import type { SiteDetail } from '../../types/site'

/** Which pane of the detail page is on screen. */
type DetailTab = 'overview' | 'logs' | 'ssl'

/** The lifecycle action awaiting confirmation, or `null` when none is. */
type PendingAction = 'enable' | 'disable' | 'delete' | null

/** Props accepted by this page, bound from the route. */
const props = defineProps<{
  /** The site's identity, from `/sites/:id`. */
  id: string
}>()

const { t } = useI18n()
const router = useRouter()
const store = useSitesStore()
const localeStore = useLocaleStore()

/** The tab currently shown. */
const tab: Ref<DetailTab> = ref('overview')

/** Which action the operator has started and is being asked to confirm. */
const pending: Ref<PendingAction> = ref(null)

/** The PHP version selected in the rebind picker, seeded from the site once it loads. */
const chosenPhpVersion: Ref<string> = ref('')

/**
 * The site on screen once the panel has answered with it; `null` in every other state the store
 * reports (loading, not found, failed), which the template renders from the store's `detail` itself.
 */
const site: ComputedRef<SiteDetail | null> = computed(() => {
  return store.detail.state === 'found' ? store.detail.site : null
})

/** Whether the site is still on its way, so the overview draws placeholders. */
const isLoading: ComputedRef<boolean> = computed(() => {
  return store.detail.state === 'loading'
})

/**
 * Where the site's read stands, for the loading region. A site the panel says does not exist and
 * a read that failed both leave nothing to show, so both are the region's error: the empty state
 * or the alert.
 */
const regionState: ComputedRef<LoadingState> = computed(() => {
  switch (store.detail.state) {
    case 'loading':
      return 'loading'
    case 'found':
      return 'loaded'
    default:
      return 'error'
  }
})

/** The three panes, in the order the design lists them. */
const tabOptions: ComputedRef<SegmentOption[]> = computed(() => {
  return [
    { value: 'overview', label: t('sites.detail.tabs.overview') },
    { value: 'logs', label: t('sites.detail.tabs.logs') },
    { value: 'ssl', label: t('sites.detail.tabs.ssl') },
  ]
})

/** The confirmation's title, naming the domain and what would be done to it. */
const confirmationTitle: ComputedRef<string> = computed(() => {
  const domain = site.value?.domain ?? ''
  switch (pending.value) {
    case 'enable':
      return t('sites.detail.confirmEnableTitle', { domain })
    case 'disable':
      return t('sites.detail.confirmDisableTitle', { domain })
    default:
      return t('sites.detail.confirmDeleteTitle', { domain })
  }
})

/** The confirm button's label, naming the action rather than a bare "Confirm" (#218). */
const confirmationLabel: ComputedRef<string> = computed(() => {
  switch (pending.value) {
    case 'enable':
      return t('sites.detail.confirmEnableAction')
    case 'disable':
      return t('sites.detail.confirmDisableAction')
    default:
      return t('sites.detail.confirmDeleteAction')
  }
})

/** The consequence the confirmation asks the operator to weigh. */
const confirmationText: ComputedRef<string> = computed(() => {
  switch (pending.value) {
    case 'enable':
      return t('sites.detail.confirmEnable')
    case 'disable':
      return t('sites.detail.confirmDisable')
    default:
      return t('sites.detail.confirmDelete')
  }
})

/** Whether the loaded site runs on PHP, which is the only case a runtime can be rebound in. */
const isPhpSite: ComputedRef<boolean> = computed(() => {
  return site.value?.backendType === 'php'
})

/** Whether the rebind picker currently names a different version than the site is bound to. */
const canApplyPhpVersion: ComputedRef<boolean> = computed(() => {
  const current = site.value
  return current !== null && chosenPhpVersion.value.length > 0 && chosenPhpVersion.value !== current.phpVersion
})

/**
 * Switches pane. Leaving the log tab unmounts it, and its own unmount hook stops the stream.
 * @param value The tab the control reported.
 * @returns Nothing.
 */
const onTabChange = (value: string): void => {
  tab.value = value as DetailTab
}

/**
 * Starts an action, which then waits for confirmation.
 * @param action The action the operator clicked.
 * @returns Nothing.
 */
const ask = (action: Exclude<PendingAction, null>): void => {
  pending.value = action
}

/**
 * Abandons a pending action.
 * @returns Nothing.
 */
const cancel = (): void => {
  pending.value = null
}

/**
 * Carries out the confirmed action. A deletion leaves for the list, because the page it was on
 * no longer describes anything.
 * @returns Resolves once the request has settled.
 */
const confirm = async (): Promise<void> => {
  const action = pending.value

  if (action === 'enable') {
    await store.enable(props.id)
  } else if (action === 'disable') {
    await store.disable(props.id)
  } else if (action === 'delete' && (await store.remove(props.id))) {
    await router.push({ name: 'sites' })
  }

  // Closed after the request settles rather than before it is sent: the dialog is
  // what tells the operator the panel is working, and it must outlive the wait.
  pending.value = null
}

/**
 * Rebinds the site to the version chosen in the picker.
 * @returns Resolves once the request has settled.
 */
const applyPhpVersion = async (): Promise<void> => {
  await store.changePhpVersion(props.id, chosenPhpVersion.value)
}

/**
 * Loads the site and, when it is a PHP site, the runtimes it could be rebound to.
 * @returns Resolves once the requests have settled.
 */
const load = async (): Promise<void> => {
  await store.loadOne(props.id)
  const loaded = site.value
  if (loaded !== null && loaded.backendType === 'php') {
    chosenPhpVersion.value = loaded.phpVersion
    await store.loadPhpVersions()
  }
}

onMounted(load)

// Navigating away from this page while a tail is open would leave the connection running for
// the life of the tab. The log tab stops its own stream on unmount; this is the belt to that
// braces, because the page can also be left while the log tab was never mounted at all.
onBeforeUnmount(() => {
  store.stopLogTail()
})
</script>

<template>
  <section class="flex w-full min-w-0 flex-col gap-4">
    <UiLoadingRegion :state="regionState">
      <!-- A 404 is the not-found state, never the alert: the panel answered, and what it said is
           that no such site exists for the caller (issue #204). -->
      <template #error>
        <UiAlert v-if="store.detail.state === 'failed'" variant="error">
          {{ store.detail.message ?? t('app.status.unreachable') }}
        </UiAlert>
        <UiEmptyState
          v-else
          :title="t('sites.detail.notFoundTitle')"
          :description="t('sites.detail.notFoundDescription')"
        />
      </template>

      <div class="flex min-w-0 flex-col gap-4">
        <UiPageHeading
          mono
          :eyebrow="t('sites.list.heading')"
          :title="site?.domain ?? ''"
          :subtitle="site === null ? '' : t(`sites.backendType.${site.backendType}`)"
          :loading="isLoading"
        >
          <template #actions>
            <SiteStatusBadge v-if="site !== null" :status="site.status" />
            <UiLink :to="{ name: 'sites' }" icon="chevronLeft">{{ t('sites.detail.backToList') }}</UiLink>
          </template>
        </UiPageHeading>

        <UiAlert v-if="!isLoading && store.errorMessage !== null" variant="error">
          {{ store.errorMessage }}
        </UiAlert>

        <UiSegmentedControl
          :model-value="tab"
          :options="tabOptions"
          :label="t('sites.detail.heading')"
          @update:model-value="onTabChange"
        />

        <div v-if="tab === 'overview'" class="grid min-w-0 gap-4 lg:grid-cols-3">
          <UiCard class="lg:col-span-2">
            <!-- The values are read only once loaded: while loading each item draws a placeholder
                 line in place of its slot. The two items only some sites have wait for the site. -->
            <UiDescriptionList :loading="isLoading">
              <UiDescriptionItem :term="t('sites.detail.domainLabel')" mono>
                {{ site?.domain }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('sites.detail.aliasesLabel')" mono>
                {{
                  site === null
                    ? ''
                    : site.aliases.length > 0
                    ? site.aliases.join(', ')
                    : t('sites.detail.noAliases')
                }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('sites.detail.backendTypeLabel')">
                {{ site === null ? '' : t(`sites.backendType.${site.backendType}`) }}
              </UiDescriptionItem>
              <UiDescriptionItem
                v-if="site !== null && site.phpVersion.length > 0"
                :term="t('sites.detail.phpVersionLabel')"
                mono
              >
                {{ site.phpVersion }}
              </UiDescriptionItem>
              <UiDescriptionItem
                v-if="site !== null && site.proxyUpstream.length > 0"
                :term="t('sites.detail.proxyUpstreamLabel')"
                mono
              >
                {{ site.proxyUpstream }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('sites.detail.documentRootLabel')" mono>
                {{ site?.documentRoot }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('sites.detail.statusLabel')">
                <SiteStatusBadge v-if="site !== null" :status="site.status" />
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('sites.detail.certificateLabel')">
                {{
                  site?.hasCertificate === true
                    ? t('sites.detail.certificateInstalled')
                    : t('sites.detail.certificateMissing')
                }}
              </UiDescriptionItem>
              <UiDescriptionItem :term="t('sites.detail.createdAtLabel')">
                {{ site === null ? '' : formatDate(site.createdAt, localeStore.current) }}
              </UiDescriptionItem>
            </UiDescriptionList>
          </UiCard>

          <div class="flex min-w-0 flex-col gap-4">
            <UiCard v-if="isPhpSite" class="flex flex-col gap-4">
              <UiSectionHeading :title="t('sites.detail.phpVersionLabel')">
                <template #action>
                  <span class="font-mono text-lg font-semibold text-text-primary tabular-nums">
                    {{ site?.phpVersion }}
                  </span>
                </template>
              </UiSectionHeading>
              <PhpVersionSelect
                v-model="chosenPhpVersion"
                :versions="store.phpVersions"
                :disabled="store.acting"
              />
              <div>
                <UiButton :disabled="!canApplyPhpVersion || store.acting" @click="applyPhpVersion">
                  {{ store.acting ? t('sites.detail.working') : t('sites.detail.changePhpVersion') }}
                </UiButton>
              </div>
            </UiCard>

            <!-- Which actions apply depends on the site's status, so none is offered before it is
                 known; a placeholder holds the card's place. -->
            <UiCard v-if="site === null">
              <UiSkeleton shape="block" />
            </UiCard>
            <UiCard v-else class="flex flex-wrap items-center gap-2">
              <UiButton v-if="site.status === 'disabled'" variant="secondary" @click="ask('enable')">
                {{ t('sites.detail.enable') }}
              </UiButton>
              <UiButton v-else variant="secondary" @click="ask('disable')">
                {{ t('sites.detail.disable') }}
              </UiButton>
              <UiButton variant="destructive" @click="ask('delete')">{{ t('common.delete') }}</UiButton>
            </UiCard>
          </div>
        </div>

        <SiteLogsTab v-else-if="tab === 'logs'" :site-id="props.id" />

        <SiteSslTab v-else-if="site !== null" :site-id="props.id" :domain="site.domain" />
      </div>
    </UiLoadingRegion>

    <!-- Outside the tab branches, and bound to a value that really changes rather than wrapped
         in a `v-if`: a dialog created with `open` already true never runs `UiModal`s
         open-watcher, so focus never enters it and Escape never reaches it. -->
    <UiConfirm
      :open="pending !== null"
      :title="confirmationTitle"
      :question="confirmationText"
      :confirm-label="confirmationLabel"
      :cancel-label="t('common.cancel')"
      :close-label="t('common.close')"
      :acting="store.acting"
      :acting-label="t('sites.detail.working')"
      :destructive="pending !== 'enable'"
      @close="cancel"
      @confirm="confirm"
    />
  </section>
</template>
