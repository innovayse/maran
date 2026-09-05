<script setup lang="ts">
/**
 * One site, in three tabs: what it is, what it is logging, and what certifies it.
 *
 * Every action asks first, and the question names the consequence rather than asking the
 * operator to repeat themselves. Deletion in particular says exactly what the contract does:
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
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiSegmentedControl, { type SegmentOption } from '../../components/ui/UiSegmentedControl.vue'
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

/** The sentence shown while an action awaits confirmation. */
const confirmationText: ComputedRef<string> = computed(() => {
  switch (pending.value) {
    case 'enable':
      return t('sites.detail.confirmEnable')
    case 'disable':
      return t('sites.detail.confirmDisable')
    case 'delete':
      return t('sites.detail.confirmDelete')
    default:
      return ''
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
  pending.value = null

  if (action === 'enable') {
    await store.enable(props.id)
  } else if (action === 'disable') {
    await store.disable(props.id)
  } else if (action === 'delete' && (await store.remove(props.id))) {
    await router.push({ name: 'sites' })
  }
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
      <div class="mb-4 flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 class="font-mono text-3xl font-semibold tracking-title text-text-primary">
            {{ store.selected.domain }}
          </h1>
          <p class="mt-1 text-base text-text-secondary">
            {{ t(`sites.backendType.${store.selected.backendType}`) }}
          </p>
        </div>
        <div class="flex items-center gap-2">
          <SiteStatusBadge :status="store.selected.status" />
          <UiButton variant="ghost" @click="router.push({ name: 'sites' })">
            {{ t('sites.detail.backToList') }}
          </UiButton>
        </div>
      </div>

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
          <dl class="grid gap-3 sm:grid-cols-2">
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.domainLabel') }}</dt>
              <dd class="font-mono text-base text-text-primary">{{ store.selected.domain }}</dd>
            </div>
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.aliasesLabel') }}</dt>
              <dd class="font-mono text-base text-text-primary">
                {{
                  store.selected.aliases.length > 0
                    ? store.selected.aliases.join(', ')
                    : t('sites.detail.noAliases')
                }}
              </dd>
            </div>
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.backendTypeLabel') }}</dt>
              <dd class="text-base text-text-primary">
                {{ t(`sites.backendType.${store.selected.backendType}`) }}
              </dd>
            </div>
            <div v-if="store.selected.phpVersion.length > 0">
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.phpVersionLabel') }}</dt>
              <dd class="font-mono text-base text-text-primary">{{ store.selected.phpVersion }}</dd>
            </div>
            <div v-if="store.selected.proxyUpstream.length > 0">
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.proxyUpstreamLabel') }}</dt>
              <dd class="font-mono text-base text-text-primary">{{ store.selected.proxyUpstream }}</dd>
            </div>
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.documentRootLabel') }}</dt>
              <dd class="font-mono text-base text-text-primary">{{ store.selected.documentRoot }}</dd>
            </div>
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.statusLabel') }}</dt>
              <dd><SiteStatusBadge :status="store.selected.status" /></dd>
            </div>
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.certificateLabel') }}</dt>
              <dd class="text-base text-text-primary">
                {{
                  store.selected.hasCertificate
                    ? t('sites.detail.certificateInstalled')
                    : t('sites.detail.certificateMissing')
                }}
              </dd>
            </div>
            <div>
              <dt class="text-sm text-text-secondary">{{ t('sites.detail.createdAtLabel') }}</dt>
              <dd class="text-base text-text-primary">
                {{ formatDate(store.selected.createdAt, localeStore.current) }}
              </dd>
            </div>
          </dl>
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
          <template v-if="pending !== null">
            <span class="text-base text-text-secondary">{{ confirmationText }}</span>
            <UiButton variant="destructive" :disabled="store.acting" @click="confirm">
              {{ store.acting ? t('sites.detail.working') : t('sites.detail.confirm') }}
            </UiButton>
            <UiButton variant="secondary" @click="cancel">{{ t('sites.detail.cancel') }}</UiButton>
          </template>

          <template v-else>
            <UiButton v-if="store.selected.status === 'disabled'" variant="secondary" @click="ask('enable')">
              {{ t('sites.detail.enable') }}
            </UiButton>
            <UiButton v-else variant="secondary" @click="ask('disable')">
              {{ t('sites.detail.disable') }}
            </UiButton>
            <UiButton variant="destructive" @click="ask('delete')">{{ t('sites.detail.delete') }}</UiButton>
          </template>
        </div>
      </template>

      <SiteLogsTab v-else-if="tab === 'logs'" :site-id="props.id" />

      <SiteSslTab v-else :site-id="props.id" :domain="store.selected.domain" />
    </template>

    <UiAlert v-else-if="store.errorMessage !== null" variant="error">{{ store.errorMessage }}</UiAlert>

    <UiEmptyState
      v-else
      :title="t('sites.detail.notFoundTitle')"
      :description="t('sites.detail.notFoundDescription')"
    />
  </section>
</template>
