const mobileUserAgent = /Android|iPhone|iPad|iPod|Windows Phone|BlackBerry|Opera Mini|Opera Mobi|Mobile/i

export function isMobileUserAgent(userAgent?: string) {
  const value = userAgent ?? (typeof navigator === 'undefined' ? '' : navigator.userAgent)
  return mobileUserAgent.test(value)
}
