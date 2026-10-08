import { describe, expect, it } from 'vitest'
import { isMobileUserAgent } from './platform'

describe('isMobileUserAgent', () => {
  it('识别常见手机浏览器', () => {
    expect(isMobileUserAgent('Mozilla/5.0 (Linux; Android 14; Pixel 8) Mobile')).toBe(true)
    expect(isMobileUserAgent('Mozilla/5.0 (iPhone; CPU iPhone OS 17_0 like Mac OS X)')).toBe(true)
  })

  it('保留桌面浏览器', () => {
    expect(isMobileUserAgent('Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130.0 Safari/537.36')).toBe(false)
  })
})
