<script setup lang="ts">
/**
 * The backup destinations screen: where this server records that its copies live. Renders a
 * `<section>`, not a `<main>` — the single `<main>` landmark lives in the layout this page is
 * nested under. State comes exclusively from the destinations store; the page never touches the API
 * layer (rules/vue.md: API composables are called from stores only).
 *
 * **The screen reads, and offers nothing to add or remove, because the panel can do neither.**
 * `BackupDestinationsController` publishes no delete at all, and its `POST` has no success arm on
 * this build: every request it can be given is answered with a refusal, for a remote destination or
 * for a second local one. A control here would be a promise the product cannot keep — the same rule
 * the restore dialog follows when it offers restore on a completed backup and on no other, read in
 * the other direction.
 *
 * **The remote kind is described rather than hidden, and described rather than offered.** The panel
 * knows two kinds of storage and can act on one; the "kinds" section below says exactly that, with
 * no control attached. Leaving it out would make the SPA quieter than the backend, which publishes
 * a named code and a localized sentence precisely so that "you asked for S3 and this build cannot"
 * is sayable. Putting a button on it would make the SPA louder than the backend, which stores
 * nothing.
 *
 * **What the screen must not imply.** A destination is not a choice being offered; there is one,
 * and it is the default that every backup taken so far already went to. Its path is what the agent
 * said on this request and not an instruction — the agent refuses to be told a root and writes to
 * its own — so it is shown as machine text, is not editable anywhere in the panel, and is stated as
 * not established when the panel could not ask. The copies there are not encrypted at rest. All
 * three are said on the screen, because a page listing a storage location reads as a page where
 * storage is configured.
 *
 * **No credential is displayed, because none is received.** No destination on this build holds one;
 * `BackupDestination` declares no secret member, so a key the server one day started sending would
 * not reach this template at all.
 */
import { computed, onMounted, type ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../../components/ui/UiAlert.vue'
import UiBadge from '../../components/ui/UiBadge.vue'
import UiCard from '../../components/ui/UiCard.vue'
import UiDescriptionItem from '../../components/ui/UiDescriptionItem.vue'
import UiDescriptionList from '../../components/ui/UiDescriptionList.vue'
import UiEmptyState from '../../components/ui/UiEmptyState.vue'
import UiIcon from '../../components/ui/UiIcon.vue'
import UiPageHeading from '../../components/ui/UiPageHeading.vue'
import UiSectionHeading from '../../components/ui/UiSectionHeading.vue'
import UiSpinner from '../../components/ui/UiSpinner.vue'
import { useBackupDestinationKindText } from '../../composables/useBackupDestinationKindText'
import { useBackupDestinationsStore } from '../../stores/backupDestinations'
import { useLocaleStore } from '../../stores/locale'
import { formatIsoTimestamp } from '../../utils/formatIsoTimestamp'
import type { BackupDestinationKind } from '../../types/backupDestination'

/**
 * The kinds of storage the panel knows about, and whether this build can write to one.
 *
 * `admits` is a client-side statement about a CAPABILITY, never a security decision: the panel
 * refuses a remote destination at two boundaries of its own regardless of what this bundle
 * believes. It is written so the screen can only ever promise LESS than the server does — a build
 * that quietly gained the remote arm would be understated here until this line changed, which is
 * the harmless direction. The reverse, a screen claiming S3 works, is the one that costs somebody
 * their backups.
 */
const KINDS: readonly { kind: BackupDestinationKind; admits: boolean }[] = [
  { kind: 'local', admits: true },
  { kind: 's3', admits: false },
]

const { t } = useI18n()
const kindText = useBackupDestinationKindText()
const store = useBackupDestinationsStore()
const localeStore = useLocaleStore()

/** Whether the panel answered successfully and reported no destination at all. */
const isEmpty: ComputedRef<boolean> = computed(() => {
  return store.isLoaded && store.errorMessage === null && store.destinations.length === 0
})

/**
 * Renders a destination's timestamp in the interface language.
 * @param at The ISO-8601 instant the panel reported.
 * @returns The formatted timestamp.
 */
const recordedAt = (at: string): string => {
  return formatIsoTimestamp(at, localeStore.current)
}

onMounted(() => {
  void store.load()
})
</script>

<template>
  <section class="w-full max-w-2xl">
    <UiPageHeading
      class="mb-4"
      :title="t('backups.destinations.heading')"
      :subtitle="t('backups.destinations.subtitle')"
      :note="t('backups.destinations.readOnlyNote')"
    />

    <UiAlert v-if="store.errorMessage !== null" variant="error" class="mb-4">
      {{ store.errorMessage }}
    </UiAlert>

    <UiSpinner v-if="store.loading" :label="t('backups.destinations.loading')" />

    <template v-else-if="store.isLoaded">
      <UiEmptyState
        v-if="isEmpty"
        class="mb-6"
        :title="t('backups.destinations.emptyTitle')"
        :description="t('backups.destinations.emptyDescription')"
      >
        <template #icon>
          <UiIcon name="archive" aria-hidden="true" />
        </template>
      </UiEmptyState>

      <UiCard v-for="destination in store.destinations" :key="destination.id" class="mb-4">
        <div class="mb-3 flex items-center gap-2">
          <!-- The heading is the backend's localized name; the stored one is shown as a field
               below whenever the two differ. They differ for exactly one row — the destination this
               panel seeded and named itself in English — so an operator-named destination is
               headed by the words the operator typed, never by a translation of them. -->
          <UiSectionHeading :title="destination.displayName" />
          <UiBadge v-if="destination.isDefault" variant="info">
            {{ t('backups.destinations.default') }}
          </UiBadge>
        </div>
        <UiDescriptionList>
          <UiDescriptionItem :term="t('backups.destinations.fields.kind')">
            {{ kindText.label(destination.kind) }}
          </UiDescriptionItem>
          <!-- Only when the heading is not already this value. The stored name is what the row
               holds, what `psql` prints and what a support ticket names, so it must stay reachable
               once the heading starts showing a translation of it — and repeating it under a
               heading that already says it would be noise on every operator-named row. -->
          <UiDescriptionItem
            v-if="destination.displayName !== destination.name"
            :term="t('backups.destinations.fields.storedName')"
            mono
          >
            {{ destination.name }}
          </UiDescriptionItem>
          <!-- Shown for a local destination whether or not the path is known. The row is keyed on
               the KIND, not on the value: "we could not ask the agent where it writes" is the fact
               an operator most needs during the incident that stopped the agent, and a row that
               vanished instead would read as a screen that had simply lost interest. -->
          <UiDescriptionItem
            v-if="destination.kind === 'local'"
            :term="t('backups.destinations.fields.path')"
            :mono="destination.path !== null"
          >
            <template v-if="destination.path !== null">{{ destination.path }}</template>
            <span v-else class="text-text-secondary">{{
              t('backups.destinations.pathUnestablished')
            }}</span>
          </UiDescriptionItem>
          <UiDescriptionItem :term="t('backups.destinations.fields.recordedAt')" mono>
            {{ recordedAt(destination.createdAt) }}
          </UiDescriptionItem>
        </UiDescriptionList>
        <p v-if="destination.isDefault" class="mt-3 text-sm text-text-secondary">
          {{ t('backups.destinations.defaultHint') }}
        </p>
      </UiCard>

      <UiCard class="mb-4">
        <UiSectionHeading
          class="mb-3"
          :title="t('backups.destinations.kindsHeading')"
          :subtitle="t('backups.destinations.kindsSubtitle')"
        />
        <!-- Both kinds the panel knows about, and which of them this build writes to. There is no
             control here on purpose: the endpoint that would record a destination refuses every
             request it can be given, so a button would offer a choice that does not exist. -->
        <div v-for="entry in KINDS" :key="entry.kind" class="mb-3 last:mb-0">
          <div class="flex items-center gap-2">
            <span class="text-sm font-semibold text-text-primary">{{ kindText.label(entry.kind) }}</span>
            <UiBadge :variant="entry.admits ? 'success' : 'neutral'">
              {{
                entry.admits
                  ? t('backups.destinations.available')
                  : t('backups.destinations.unavailable')
              }}
            </UiBadge>
          </div>
          <p class="mt-1 text-sm text-text-secondary">{{ kindText.description(entry.kind) }}</p>
        </div>
      </UiCard>

      <UiCard>
        <UiSectionHeading class="mb-3" :title="t('backups.destinations.aboutHeading')" />
        <!-- Each of these is something a page listing a storage location is read as promising and
             does not. They are on the screen rather than in a doc comment because the operator
             reading a path is the person who would otherwise believe all three. -->
        <p class="text-sm text-text-secondary">{{ t('backups.destinations.about.notEditable') }}</p>
        <p class="mt-2 text-sm text-text-secondary">
          {{ t('backups.destinations.about.notEncrypted') }}
        </p>
        <p class="mt-2 text-sm text-text-secondary">{{ t('backups.destinations.about.adminOnly') }}</p>
      </UiCard>
    </template>
  </section>
</template>
