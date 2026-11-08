<script setup lang="ts">
/**
 * The administrator's shortcuts, as one row of links across the top of the dashboard: the things an
 * operator does most, each one click from the screen that does it. Creating something opens the
 * owning screen on its own form (`?action=create`), so the dashboard holds no form of its own.
 *
 * Every shortcut is a real link (`UiButton to`), reachable and named for a keyboard and a screen
 * reader; none calls the router from a handler. Which shortcuts appear, and in what order, is the
 * person's layout resolved by the page (only those the licence permits), so nothing here decides
 * policy. The row wraps instead of scrolling sideways, so it fits at 390px in every language.
 */
import { useI18n } from 'vue-i18n'
import type { QuickAction } from '../../types/quickAction'
import UiButton from '../ui/UiButton.vue'
import UiCard from '../ui/UiCard.vue'
import UiIcon from '../ui/UiIcon.vue'
import UiSectionHeading from '../ui/UiSectionHeading.vue'

defineProps<{
  /** The shortcuts to draw, already filtered by the licence and ordered by the person's layout. */
  actions: QuickAction[]
}>()

const { t } = useI18n()
</script>

<template>
  <UiCard data-testid="quick-actions-bar">
    <UiSectionHeading class="mb-3" :title="t('app.status.quickActions.title')" />
    <ul class="m-0 flex list-none flex-wrap gap-2 p-0">
      <li v-for="action in actions" :key="action.key">
        <UiButton :to="action.to" variant="secondary" :data-testid="`quick-action-${action.key}`">
          <UiIcon :name="action.icon" />
          {{ action.label }}
        </UiButton>
      </li>
    </ul>
  </UiCard>
</template>
