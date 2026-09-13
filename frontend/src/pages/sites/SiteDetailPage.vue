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
 * Renders a `<section>`, not a `<main>` — the layout owns the landmark.
 */
import { computed, onBeforeUnmount, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiConfirm from '../../components/ui/UiConfirm.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import PhpVersionSelect from '../../components/sites/PhpVersionSelect.vue'
import SiteStatusBadge from '../../components/sites/SiteStatusBadge.vue'
import SiteLogsTab from './SiteLogsTab.vue'
import SiteSslTab from './SiteSslTab.vue'
import { useLocaleStore } from '../../stores/locale'
import { useSitesStore } from '../../stores/sites'
import { formatDate } from '../../utils/formatDate'

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
  const domain = store.selected?.domain ?? ''
  switch (pending.value) {
    case 'enable':
      return t('sites.detail.confirmEnableTitle', { domain })
    case 'disable':
      return t('sites.detail.confirmDisableTitle', { domain })
    default:
      return t('sites.detail.confirmDeleteTitle', { domain })
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
  return store.selected?.backendType === 'php'
})

/** Whether the rebind picker currently names a different version than the site is bound to. */
const canApplyPhpVersion: ComputedRef<boolean> = computed(() => {
  const selected = store.selected
  return (
    selected !== null && chosenPhpVersion.value.length > 0 && chosenPhpVersion.value !== selected.phpVersion
  )
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
  const selected = store.selected
  if (selected !== null && selected.backendType === 'php') {
    chosenPhpVersion.value = selected.phpVersion
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
  <section class="w-full">
    <UiSpinner v-if="store.loading" :label="t('sites.detail.loading')" />

    <template v-else-if="store.selected !== null">
      <UiPageHeading
        class="mb-4"
        mono
        :title="store.selected.domain"
        :subtitle="t(`sites.backendType.${store.selected.backendType}`)"
      >
        <template #actions>
          <SiteStatusBadge :status="store.selected.status" />
          <UiButton variant="ghost" @click="router.push({ name: 'sites' })">
            {{ t('sites.detail.backToList') }}
          </UiButton>
        </template>
      </UiPageHeading>

      <UiAlert v-if="store.errorMessage !== null" variant="error" class="mb-4">
        {{ store.errorMessage }}
      </UiAlert>

      <div class="mb-4">
        <UiSegmentedControl
          :model-value="tab"
          :options="tabOptions"
          :label="t('sites.detail.heading')"
          @update:model-value="onTabChange"
        />
      </div>

      <template v-if="tab === 'overview'">
        <UiCard>
          <UiDescriptionList>
            <UiDescriptionItem :term="t('sites.detail.domainLabel')" mono>
              {{ store.selected.domain }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('sites.detail.aliasesLabel')" mono>
              {{
                store.selected.aliases.length > 0
                  ? store.selected.aliases.join(', ')
                  : t('sites.detail.noAliases')
              }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('sites.detail.backendTypeLabel')">
              {{ t(`sites.backendType.${store.selected.backendType}`) }}
            </UiDescriptionItem>
            <UiDescriptionItem
              v-if="store.selected.phpVersion.length > 0"
              :term="t('sites.detail.phpVersionLabel')"
              mono
            >
              {{ store.selected.phpVersion }}
            </UiDescriptionItem>
            <UiDescriptionItem
              v-if="store.selected.proxyUpstream.length > 0"
              :term="t('sites.detail.proxyUpstreamLabel')"
              mono
            >
              {{ store.selected.proxyUpstream }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('sites.detail.documentRootLabel')" mono>
              {{ store.selected.documentRoot }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('sites.detail.statusLabel')">
              <SiteStatusBadge :status="store.selected.status" />
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('sites.detail.certificateLabel')">
              {{
                store.selected.hasCertificate
                  ? t('sites.detail.certificateInstalled')
                  : t('sites.detail.certificateMissing')
              }}
            </UiDescriptionItem>
            <UiDescriptionItem :term="t('sites.detail.createdAtLabel')">
              {{ formatDate(store.selected.createdAt, localeStore.current) }}
            </UiDescriptionItem>
          </UiDescriptionList>
        </UiCard>

        <UiCard v-if="isPhpSite" class="mt-4">
          <div class="flex flex-col gap-3">
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
          </div>
        </UiCard>

        <div class="mt-4 flex flex-wrap items-center gap-2">
          <UiButton v-if="store.selected.status === 'disabled'" variant="secondary" @click="ask('enable')">
            {{ t('sites.detail.enable') }}
          </UiButton>
          <UiButton v-else variant="secondary" @click="ask('disable')">
            {{ t('sites.detail.disable') }}
          </UiButton>
          <UiButton variant="destructive" @click="ask('delete')">{{ t('common.delete') }}</UiButton>
        </div>
      </template>

      <SiteLogsTab v-else-if="tab === 'logs'" :site-id="props.id" />

      <SiteSslTab v-else :site-id="props.id" :domain="store.selected.domain" />

      <!-- Outside the tab branches, and bound to a value that really changes rather than wrapped
           in a `v-if`: a dialog created with `open` already true never runs `UiModal`s
           open-watcher, so focus never enters it and Escape never reaches it. -->
      <UiConfirm
        :open="pending !== null"
        :title="confirmationTitle"
        :question="confirmationText"
        :confirm-label="t('common.confirm')"
        :cancel-label="t('common.cancel')"
        :close-label="t('common.close')"
        :acting="store.acting"
        :acting-label="t('sites.detail.working')"
        :destructive="pending !== 'enable'"
        @close="cancel"
        @confirm="confirm"
      />
    </template>

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else
      :title="t('sites.detail.notFoundTitle')"
      :description="t('sites.detail.notFoundDescription')"
    />
  </section>
</template>
