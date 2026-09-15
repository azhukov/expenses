/** The environment variable the preview server reads its public host names from. */
export const PREVIEW_HOSTS_SETTING = 'PREVIEW_ALLOWED_HOSTS'

/**
 * The host names, beyond localhost, that the preview server answers. Vite refuses a request whose
 * `Host` it does not recognise, so a server published under a public domain (Railway's) serves
 * nothing but 403s until that domain is listed. Read by `vite.config.ts` when the server starts, so
 * this module must stay free of anything browser-only.
 *
 * Comma-separated; whitespace around entries and empty entries are ignored. An entry that is a URL
 * or carries a port is refused here, at startup, because Vite would otherwise take it silently and
 * never match it.
 */
export function parsePreviewHosts(value: string | undefined): string[] {
  const hosts = (value ?? '')
    .split(',')
    .map(entry => entry.trim())
    .filter(entry => entry !== '')

  const refused = hosts.find(host => /[/:\s]/.test(host))

  if (refused !== undefined) {
    throw new Error(
      `${PREVIEW_HOSTS_SETTING} entry '${refused}' is not a host name. List bare names separated by commas, e.g. ${PREVIEW_HOSTS_SETTING}=expenses.example.com, with no scheme, port or path.`,
    )
  }

  return hosts
}
