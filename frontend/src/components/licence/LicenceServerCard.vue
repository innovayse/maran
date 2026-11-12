<script setup lang="ts">
/**
 * This server's ID, as a licence names it to be bound here, with a copy control and the link to the
 * marketplace page where a licence is claimed for it (issue #536).
 *
 * The ID and the link arrive from the backend, which reads the ID through the same seam the licence is
 * judged against and builds the link; this component only shows them. The ID is selectable text beside
 * the copy control, because the clipboard is blocked in some browsers. When the panel could not read the
 * ID it says so and offers neither a copy control nor a link. It holds no request: the page hands it the
 * two values.
 */
import { onBeforeUnmount, ref, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'
import UiAlert from '../ui/UiAlert.vue'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiDescriptionItem from '../ui/UiDescriptionItem.vue'
import UiDescriptionList from '../ui/UiDescriptionList.vue'
import UiExternalLink from '../ui/UiExternalLink.vue'
import UiFieldHint from '../ui/UiFieldHint.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiLiveRegion from '../ui/UiLiveRegion.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'
import UiStack from '../ui/UiStack.vue'

/** Props accepted by {@link LicenceServerCard}. */
defineProps<{
  /** This server's ID, or `null` when the panel could not read it. */
  serverId: string | null
  /** The marketplace page that claims a licence for this server, or `null` when none can be made. */
  claimUrl: string | null
}>()

const { t } = useI18n()

/** How long the control says "copied" before it offers the copy again, in milliseconds. */
const COPIED_SHOWN_MS = 3000

/** Whether the ID was copied a moment ago, so the control can say so. */
const copied: Ref<boolean> = ref(false)

/** The pending reset of {@link copied}, or `null` when none is waiting. */
let resetTimer: ReturnType<typeof setTimeout> | null = null

/**
 * Cancels the pending reset of {@link copied}, if one is waiting.
 * @returns Nothing.
 */
const cancelReset = (): void => {
  if (resetTimer !== null) {
    clearTimeout(resetTimer)
    resetTimer = null
  }
}

/**
 * Copies the ID to the clipboard. A failure is swallowed on purpose and leaves the control unchanged:
 * the clipboard is blocked in some browsers, and the ID stays selectable beside it.
 * @param id The ID to copy.
 * @returns Resolves once the clipboard write has settled, successfully or not.
 */
const copyId = async (id: string): Promise<void> => {
  cancelReset()
  try {
    await navigator.clipboard.writeText(id)
    copied.value = true
    resetTimer = setTimeout(() => {
      copied.value = false
      resetTimer = null
    }, COPIED_SHOWN_MS)
  } catch {
    copied.value = false
  }
}

onBeforeUnmount(cancelReset)
</script>

<template>
  <UiCard data-testid="licence-server">
    <UiSectionHeading class="mb-4" :title="t('licence.server.title')" :subtitle="t('licence.server.intro')" />

    <UiStack v-if="serverId !== null">
      <UiDescriptionList>
        <UiDescriptionItem :term="t('licence.server.idLabel')" mono data-testid="licence-server-id">
          {{ serverId }}
        </UiDescriptionItem>
      </UiDescriptionList>

      <UiStack direction="row" align="center" wrap>
        <UiButton variant="secondary" @click="copyId(serverId)">
          <UiIcon :name="copied ? 'check' : 'copy'" size="sm" />
          {{ copied ? t('licence.server.copied') : t('licence.server.copy') }}
        </UiButton>
        <UiExternalLink v-if="claimUrl !== null" :href="claimUrl" data-testid="licence-claim-link">
          {{ t('licence.server.claimLink') }}
        </UiExternalLink>
      </UiStack>
      <!-- A change of a button's name is not announced by itself, so the copy result is also spoken
           from a status region that is in the page before it has anything to say. -->
      <UiLiveRegion>{{ copied ? t('licence.server.copied') : '' }}</UiLiveRegion>

      <UiFieldHint v-if="claimUrl === null" data-testid="licence-claim-unavailable">
        {{ t('licence.server.noClaimLink') }}
      </UiFieldHint>
    </UiStack>
    <UiAlert v-else variant="warning" data-testid="licence-server-unavailable">
      {{ t('licence.server.unavailable') }}
    </UiAlert>
  </UiCard>
</template>
