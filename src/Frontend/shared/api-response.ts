export type ApiEnvelope<T> = {
  statusCode: number
  statusMessage: string
  result: T
}

export type ApiError = {
  statusCode: number
  statusMessage: string
  errorCode: string
  errors?: Record<string, string[]>
  traceId?: string
}

export type Locale = 'th' | 'en'

export function unwrapResponse<T>(response: ApiEnvelope<T> | null | undefined): T {
  return response?.result as T
}

export function safeError(error: unknown, locale: Locale): string {
  const candidate = error as { data?: Partial<ApiError>; status?: number } | null
  const payload = candidate?.data
  const status = payload?.statusCode ?? candidate?.status
  const code = payload?.errorCode
  const thai = locale === 'th'
  if (code === 'INVALIDCREDENTIALS')
    return thai ? 'อีเมลหรือรหัสผ่านไม่ถูกต้อง' : 'Incorrect email or password.'
  if (code === 'MFAREQUIRED')
    return thai ? 'บัญชีนี้ต้องเข้าสู่ระบบที่หน้าผู้ดูแลระบบ' : 'This account signs in on the system administrator page.'
  if (code === 'MFAINVALID')
    return thai ? 'รหัสยืนยันไม่ถูกต้องหรือถูกใช้ไปแล้ว' : 'That code is not valid or has already been used.'
  if (code === 'DEFINITION_EXISTS')
    return thai ? 'มีรูปแบบรายงานประเภทและไฟล์นี้อยู่แล้ว' : 'A report format of this kind and file type already exists.'
  if (status === 401) return thai ? 'เซสชันหมดอายุ กรุณาเข้าสู่ระบบอีกครั้ง' : 'Session expired. Please sign in again.'
  if (status === 403) return thai ? 'คุณไม่มีสิทธิ์ทำรายการนี้' : 'You do not have permission for this action.'
  if (status === 404) return thai ? 'ไม่พบข้อมูลที่ต้องการ' : 'The requested item was not found.'
  if (status === 409) return thai ? 'ข้อมูลเปลี่ยนแปลงแล้ว กรุณาลองใหม่' : 'The data changed. Please try again.'
  if (status === 429) return thai ? 'ส่งคำขอถี่เกินไป กรุณารอสักครู่' : 'Too many requests. Please wait a moment.'
  if (code === 'VALIDATION_FAILED' || status === 400)
    return thai ? 'กรุณาตรวจสอบข้อมูลที่กรอก' : 'Please check the entered information.'
  const trace = payload?.traceId ? ` (${thai ? 'รหัสติดตาม' : 'Trace ID'}: ${payload.traceId})` : ''
  return (thai ? 'ระบบขัดข้อง กรุณาลองใหม่' : 'Something went wrong. Please try again.') + trace
}

export function fieldErrors(error: unknown, locale: Locale): Record<string, string[]> {
  const fields = (error as { data?: ApiError } | null)?.data?.errors ?? {}
  return Object.fromEntries(Object.entries(fields).map(([field, codes]) => [
    field.split('.').at(-1)!.replace(/^./, first => first.toLowerCase()),
    codes.map(code => {
      if (code === 'REQUIRED') return locale === 'th' ? 'จำเป็นต้องกรอก' : 'Required'
      if (code === 'INVALID_DATE') return locale === 'th' ? 'วันที่ไม่ถูกต้อง' : 'Invalid date'
      return locale === 'th' ? 'ข้อมูลไม่ถูกต้อง' : 'Invalid value'
    })
  ]))
}

export async function withRemoteRefresh<T>(
  send: (accessToken: string) => Promise<T>,
  currentToken: { value: string }, shellOrigin: string
): Promise<T> {
  try { return await send(currentToken.value) }
  catch (cause) {
    const status = (cause as { status?: number; data?: ApiError } | null)?.status ??
      (cause as { data?: ApiError } | null)?.data?.statusCode
    if (status !== 401 || window.parent === window) throw cause
    const renewed = await requestShellRefresh(shellOrigin)
    if (!renewed) {
      window.parent.postMessage({ type: 'aiyara.sign-in-required' }, shellOrigin)
      throw cause
    }
    currentToken.value = renewed
    return await send(renewed)
  }
}

function requestShellRefresh(shellOrigin: string): Promise<string | null> {
  return new Promise(resolve => {
    const requestId = crypto.randomUUID()
    const timeout = window.setTimeout(() => finish(null), 10_000)
    function finish(token: string | null) {
      window.clearTimeout(timeout)
      window.removeEventListener('message', receive)
      resolve(token)
    }
    function receive(event: MessageEvent) {
      if (event.origin !== shellOrigin || event.source !== window.parent ||
          event.data?.type !== 'aiyara.refresh-result' || event.data.requestId !== requestId) return
      finish(typeof event.data.token === 'string' && event.data.token ? event.data.token : null)
    }
    window.addEventListener('message', receive)
    window.parent.postMessage({ type: 'aiyara.refresh-request', requestId }, shellOrigin)
  })
}
