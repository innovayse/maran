/** The longest account name the server accepts (`CreateAccountCommandValidator`). */
const MAX_ACCOUNT_NAME_LENGTH = 30

/** The shortest account name the server accepts. */
const MIN_ACCOUNT_NAME_LENGTH = 3

/**
 * Suggests an account name for a primary domain, so the create form can fill the username in as
 * the operator types the domain (issue #194). Only a suggestion: the field stays editable and the
 * server validates whatever is finally sent.
 *
 * The name is the first label of the domain, after a leading `www.`, reduced to the server's
 * alphabet: lowercase letters and digits kept, everything else dropped, leading digits dropped so
 * the name starts with a letter, and cut to the server's maximum length.
 * @param domain The primary domain as typed.
 * @returns The suggested name, or an empty string when the domain yields nothing the server would accept.
 */
export const accountNameFromDomain = (domain: string): string => {
  const firstLabel = domain.trim().toLowerCase().replace(/^www\./, '').split('.')[0] ?? ''
  const name = firstLabel
    .replace(/[^a-z0-9]/g, '')
    .replace(/^[0-9]+/, '')
    .slice(0, MAX_ACCOUNT_NAME_LENGTH)
  return name.length >= MIN_ACCOUNT_NAME_LENGTH ? name : ''
}
