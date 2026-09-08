import { copyFile, mkdir, readFile } from 'node:fs/promises'

// 只同步管理代理已经生成的客户端，不修改共享生成目录。
const source = new URL('../../artifacts/v2/generated/web/', import.meta.url)
const destination = new URL('../src/generated/', import.meta.url)
await mkdir(destination, { recursive: true })
for (const file of ['client.ts', 'schema.d.ts']) {
  await readFile(new URL(file, source))
  await copyFile(new URL(file, source), new URL(file, destination))
}
console.log('已同步真实 OpenAPI 生成客户端及类型。')
