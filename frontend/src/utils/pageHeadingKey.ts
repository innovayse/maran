import type { InjectionKey, Ref } from 'vue'
import type { PageHeading } from '../types/pageHeading'

/**
 * What the authenticated shell provides to every `UiPageHeading` and to its breadcrumb: the
 * heading currently on screen. The heading writes itself into it and takes itself out when it
 * leaves; the breadcrumb reads it to end a record screen's trail on the record's name (issue
 * #209). `null` while no heading is mounted — a record that does not exist renders an empty state
 * instead, and the trail then ends on the screen's own title.
 */
export const PAGE_HEADING_KEY: InjectionKey<Ref<PageHeading | null>> = Symbol('page-heading')
