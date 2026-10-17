/** JWT claims are decoded for display only; authorization is enforced by the backend. */
export function readAuthClaims(token: string) {
  let claims: Record<string, unknown> = {}
  if (token) {
    try {
      const payload = token.split('.')[1] ?? ''
      const bytes = Uint8Array.from(
        atob(payload.replace(/-/g, '+').replace(/_/g, '/')),
        char => char.charCodeAt(0),
      )
      const decoded: unknown = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes))
      if (!decoded || typeof decoded !== 'object' || Array.isArray(decoded)) {
        throw new Error('Invalid JWT payload')
      }
      claims = decoded as Record<string, unknown>
    } catch (error) {
      console.warn('Unable to decode JWT display claims', error)
    }
  }

  const username = [
    claims.name,
    claims.unique_name,
    claims['http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name'],
  ].find((value): value is string => typeof value === 'string' && value.trim() !== '') ?? ''
  const displayName = typeof claims.display_name === 'string' && claims.display_name.trim()
    ? claims.display_name
    : username
  const role = claims.role ?? claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role']
  const roles = (Array.isArray(role) ? role : [role])
    .filter((value): value is string => typeof value === 'string')

  return { username, displayName, roles }
}
