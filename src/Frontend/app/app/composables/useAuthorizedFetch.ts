/** Fetches with the session's access token, renewing it once and retrying when the Gateway answers 401. */
export function useAuthorizedFetch() {
  const { token, renew } = useSession()

  return async function authorizedFetch<T>(url: string, asBlob = false, options: { method?: string; body?: object } = {}): Promise<T> {
    const send = () => $fetch<T>(url, {
      headers: { Authorization: `Bearer ${token.value}` },
      ...options,
      ...(asBlob ? { responseType: 'blob' as const } : {})
    })
    try { return await send() }
    catch (cause) {
      const status = (cause as { status?: number })?.status
      if (status !== 401 || !(await renew(true))) throw cause
      return await send()
    }
  }
}
