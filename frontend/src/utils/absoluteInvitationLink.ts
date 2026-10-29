/**
 * Turns the link the panel sent into one an administrator can hand over.
 *
 * The panel builds an absolute link only when the installation has a configured public address.
 * Without one it sends a root-relative path, because the server has no honest way to know what
 * address the administrator reached it by — but the page the administrator is looking at does, so
 * the origin is added here, from the caller, rather than guessed on either side.
 * @param link The link exactly as the panel sent it.
 * @param origin The origin the panel is being viewed from, for example `https://panel.example.com`.
 * @returns An absolute link: the input untouched when it already is one.
 */
export const absoluteInvitationLink = (link: string, origin: string): string => {
  return link.startsWith('/') ? `${origin}${link}` : link
}
