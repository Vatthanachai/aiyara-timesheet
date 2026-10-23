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
    return thai ? 'กรอกรหัส 6 หลักจากแอปยืนยันตัวตน หรือใช้รหัสกู้คืน' : 'Enter the code from your authenticator app, or use a recovery code.'
  if (code === 'MFAINVALID')
    return thai ? 'รหัสไม่ถูกต้อง หมดอายุ หรือถูกใช้ไปแล้ว ลองรหัสใหม่จากแอป หากกรอกผิดติดกัน 5 ครั้ง บัญชีจะถูกล็อก 15 นาที'
      : 'That code is not valid, has expired, or was already used. Try the current code from your app. Five wrong codes in a row lock the account for 15 minutes.'
  if (code === 'MFA_ENROLLMENT_REQUIRED')
    return thai ? 'องค์กรของคุณกำหนดให้ใช้แอปยืนยันตัวตน กรุณาตั้งค่าก่อนเข้าสู่ระบบ' : 'Your organization requires an authenticator app. Set one up to sign in.'
  if (code === 'MFA_NOT_REQUIRED')
    return thai ? 'องค์กรของคุณไม่ได้กำหนดให้ใช้แอปยืนยันตัวตน' : 'Your organization does not require an authenticator app.'
  if (code === 'MFA_ALREADY_ENROLLED')
    return thai ? 'บัญชีนี้ตั้งค่าแอปยืนยันตัวตนไว้แล้ว' : 'An authenticator app is already set up for this account.'
  if (code === 'MFA_NOT_ENROLLED')
    return thai ? 'บัญชีนี้ยังไม่ได้ตั้งค่าแอปยืนยันตัวตน กรุณาตั้งค่าก่อน' : 'No authenticator app is set up for this account yet. Set one up first.'
  if (code === 'MFA_REQUIRED_BY_ORGANIZATION')
    return thai ? 'องค์กรของคุณกำหนดให้ใช้แอปยืนยันตัวตน จึงปิดการใช้งานไม่ได้' : 'Your organization requires an authenticator app, so it cannot be turned off.'
  if (code === 'CANNOT_CHANGE_SELF')
    return thai ? 'ทำรายการนี้กับบัญชีของคุณเองไม่ได้' : 'You cannot do this to your own account.'
  if (code === 'SESSION_REVOCATION_UNAVAILABLE')
    return thai ? 'ยังไม่ได้บันทึกการเปลี่ยนแปลง เพราะออกจากระบบอุปกรณ์อื่นไม่สำเร็จ กรุณาลองใหม่' : 'The change was not saved because sessions could not be ended. Please try again.'
  if (code === 'RATELIMITED' || status === 429)
    return thai ? 'ลองถี่เกินไปหรือบัญชีถูกล็อกชั่วคราว กรุณารอสักครู่แล้วลองใหม่' : 'Too many attempts, or the account is temporarily locked. Please wait a few minutes and try again.'
  if (code === 'UNAVAILABLE' || status === 503)
    return thai ? 'ระบบยังใช้งานไม่ได้ชั่วคราว กรุณาลองใหม่ในอีกสักครู่' : 'The service is temporarily unavailable. Please try again shortly.'
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
    // A wrong or missing second-factor code is also a 401, but the session is fine: retrying with a
    // fresh token would count the same wrong code twice toward the five-attempt lockout.
    const code = (cause as { data?: ApiError } | null)?.data?.errorCode
    if (status !== 401 || code === 'MFAREQUIRED' || code === 'MFAINVALID' || window.parent === window) throw cause
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
