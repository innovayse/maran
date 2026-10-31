/**
 * What a page's heading (`UiPageHeading`) tells the shell about itself: the title it shows and
 * whether the record that title names is still loading. The app bar's breadcrumb ends on it for a
 * screen whose title is a record's name (`meta.titledByRecord`), so the trail and the page heading
 * are one string, and a record still on its way is a placeholder rather than the id in the address
 * (issue #209). A presentation shape only; never sent to or read from the backend.
 */
export interface PageHeading {
  /** The title the heading shows: the page's name, or the record's name on a record screen. */
  title: string
  /** Whether the record the title names is still loading. */
  loading: boolean
}
