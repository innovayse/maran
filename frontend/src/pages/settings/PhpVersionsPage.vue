<script setup lang="ts">
/**
 * The PHP versions this server has, and the way to the app store that adds to them.
 * Renders a `<section>`, not a `<main>` — the single `<main>` landmark lives in
 * the layout this page is nested under.
 *
 * **Installing and removing a version is the app store's work (issue #517).** A PHP version is an
 * app like the web server and Node.js, installed and removed from `/apps`, so this screen reports what
 * is installed (and the support phase, the extensions and the sites on each) and points there.
 *
 * **Administrator-only, and the server is what says so.** No route guard
 * duplicates that decision, for the reason the audit journal's route records:
 * a second copy of an authorization rule is a second place for it to be wrong,
 * and the copy in the browser is the one that cannot be trusted. A customer who
 * types this URL sees the panel's own refusal rendered on the page.
 *
 * **Layout (redesign 2026-10).** An accent eyebrow over the title, then two panels: the
 * installed versions (a table with the version in the mono face, the default marked with a badge
 * so the word is never the only carrier) and the pointer to the app store beside it from 1024px, below it
 * on a narrower screen. The table scrolls inside its own container, so nothing scrolls
 * horizontally at 390px.
 *
 * **Lifecycle and extensions (issue #157).** Each version carries its support phase as a badge (a
 * word and a glyph, never colour alone), the day its security support ends, and how many sites run
 * on it. When any site runs on an end-of-life version the page says so above the table, because
 * that is the one fact on this screen that asks for action. Choosing "Extensions" on a row opens
 * that version's extension switches in a panel of their own below; a switch is a host-wide change
 * and the panel says so.
 *
 * **A refusal to list is shown, and an empty list is only announced once the load has settled.**
 * The store records the server's refusal in `errorMessage`; the page prints it in place of the
 * table, instead of leaving a customer who typed the URL with an empty one. And
 * "No PHP version is installed" is a claim about the host, so it waits for the answer: drawn
 * before the request returned it would state, for a moment, a fact nobody had observed.
 */
import { computed, onMounted, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiButton from '../../components/ui/UiButton.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiLoadingRegion from '../../components/ui/UiLoadingRegion.vue'
import UiLink from '../../components/ui/UiLink.vue'
import UiTable from '../../components/ui/UiTable.vue'
import PhpExtensionsPanel from '../../components/sites/PhpExtensionsPanel.vue'
import PhpLifecycleBadge from '../../components/sites/PhpLifecycleBadge.vue'
import type { PhpDefaultStatus, PhpVersion } from '../../types/phpVersion'
import type { UiTableColumn } from '../../types/tableColumn'
import { useLocaleStore } from '../../stores/locale'
import { usePhpVersionsStore } from '../../stores/phpVersions'
import { formatDate } from '../../utils/formatDate'

const { t } = useI18n()
const localeStore = useLocaleStore()
const phpVersionsStore = usePhpVersionsStore()

/** The version whose extensions panel is open, e.g. `8.3`, or `null` while none is. */
const extensionsFor: Ref<string | null> = ref(null)

/** The table's columns: its header, its cells and its loading placeholder all read this one list. */
const columns: ComputedRef<UiTableColumn[]> = computed(() => {
  return [
    { key: 'version', label: t('php.table.version'), kind: 'identifier' },
    { key: 'lifecycle', label: t('php.table.lifecycle'), kind: 'badge' },
    { key: 'endOfLife', label: t('php.table.endOfLife'), kind: 'date' },
    { key: 'sitesUsing', label: t('php.table.sitesUsing'), kind: 'number' },
    { key: 'default', label: t('php.table.default'), kind: 'badge' },
    { key: 'extensions', label: t('php.table.extensions'), kind: 'actions' },
  ]
})

/** The installed versions that reached their end of life and still carry sites, in the order the server listed them. */
const endOfLifeInUse: ComputedRef<PhpVersion[]> = computed(() => {
  return phpVersionsStore.phpVersions.filter((candidate) => {
    return candidate.lifecycle.status === 'endOfLife' && (candidate.sitesUsing ?? 0) > 0
  })
})

/** How many sites run on those versions in all; the count the warning is worded by. */
const endOfLifeSites: ComputedRef<number> = computed(() => {
  return endOfLifeInUse.value.reduce((total, candidate) => {
    return total + (candidate.sitesUsing ?? 0)
  }, 0)
})

/** The end-of-life versions in use, named for the warning (`8.0, 7.4`). */
const endOfLifeVersions: ComputedRef<string> = computed(() => {
  return endOfLifeInUse.value
    .map((candidate) => {
      return candidate.version
    })
    .join(', ')
})

/**
 * Names whether a version is the host's default CLI PHP.
 *
 * Three answers, not two. `unknown` means the agent could not establish it, which is
 * not the same as "no" — printing "no" for it would state a fact nobody observed
 * (the backend keeps the two apart for exactly this reason).
 * @param status What the panel reported for this version.
 * @returns The word an operator reads.
 */
const defaultLabel = (status: PhpDefaultStatus): string => {
  switch (status) {
    case 'default':
      return t('php.table.defaultStatus.default')
    case 'notDefault':
      return t('php.table.defaultStatus.notDefault')
    case 'unknown':
      return t('php.table.defaultStatus.unknown')
  }
}

/**
 * Opens the extensions panel for one version.
 * @param chosen The installed version, e.g. `8.3`.
 * @returns Nothing; the panel loads its own list.
 */
const openExtensions = (chosen: string): void => {
  extensionsFor.value = chosen
}

onMounted(phpVersionsStore.loadPhpVersions)
</script>

<template>
  <section class="flex flex-col gap-4">
    <UiPageHeading
      :title="t('php.title')"
      :subtitle="t('php.description')"
    />

    <div class="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)] lg:items-start">
      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('php.table.heading')" />

        <!-- Administrator-only figure (sitesUsing is null for anyone else): the one fact here that asks
             for action, so it is stated before the table and not left to a badge. -->
        <UiAlert v-if="endOfLifeSites > 0" variant="warning" class="mb-4">
          {{ t('php.eol.warning', { count: endOfLifeSites, versions: endOfLifeVersions }, endOfLifeSites) }}
          <template #actions>
            <UiLink to="/sites">{{ t('php.eol.openSites') }}</UiLink>
          </template>
        </UiAlert>

        <UiLoadingRegion :state="phpVersionsStore.phpVersionsState">
          <!-- The server's own refusal or failure to list: the backend's localized sentence, so
               the SPA holds no copy of it. -->
          <template #error>
            <UiAlert variant="error">{{ phpVersionsStore.errorMessage ?? t('common.list.unreachable') }}</UiAlert>
          </template>

          <UiEmptyState
            v-if="phpVersionsStore.phpVersionsState === 'loaded' && phpVersionsStore.phpVersions.length === 0"
            :title="t('php.empty.title')"
            :description="t('php.empty.description')"
          />

          <UiTable
            v-else
            :caption="t('php.table.caption')"
            :columns="columns"
            :rows="phpVersionsStore.phpVersions"
            row-key="version"
            :loading="phpVersionsStore.phpVersionsState === 'loading'"
          >
            <template #cell-version="{ row }">{{ row.version }}</template>
            <template #cell-lifecycle="{ row }"><PhpLifecycleBadge :lifecycle="row.lifecycle" /></template>
            <template #cell-endOfLife="{ row }">
              {{ row.lifecycle.endOfLife === null ? '' : formatDate(row.lifecycle.endOfLife, localeStore.current) }}
            </template>
            <template #cell-sitesUsing="{ row }">{{ row.sitesUsing ?? '' }}</template>
            <template #cell-default="{ row }">
              <UiBadge v-if="row.defaultStatus === 'default'" variant="success">{{ defaultLabel(row.defaultStatus) }}</UiBadge>
              <UiBadge v-else-if="row.defaultStatus === 'unknown'" variant="warning">{{ defaultLabel(row.defaultStatus) }}</UiBadge>
              <template v-else>{{ defaultLabel(row.defaultStatus) }}</template>
            </template>
            <template #cell-extensions="{ row }">
              <!-- The visible word is the column's own, short enough to sit beside the version in the
                   phone's card; the version is in the accessible name, which still contains the word. -->
              <UiButton
                variant="secondary"
                :aria-label="t('php.table.openExtensions', { version: row.version })"
                @click="openExtensions(row.version)"
              >
                {{ t('php.table.extensions') }}
              </UiButton>
            </template>
          </UiTable>
        </UiLoadingRegion>
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-4" :title="t('php.manage.heading')" />
        <div class="flex flex-col gap-3">
          <p class="text-sm text-text-secondary">{{ t('php.manage.description') }}</p>
          <div>
            <UiButton variant="secondary" :to="{ name: 'apps' }">{{ t('php.manage.open') }}</UiButton>
          </div>
        </div>
      </UiCard>
    </div>

    <UiCard v-if="extensionsFor !== null">
      <UiSectionHeading
        class="mb-4"
        :title="t('phpExtensions.heading', { version: extensionsFor })"
        :subtitle="t('phpExtensions.hostWide', { version: extensionsFor })"
      />
      <PhpExtensionsPanel :version="extensionsFor" />
    </UiCard>
  </section>
</template>
