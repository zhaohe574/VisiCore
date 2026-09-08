import assert from 'node:assert/strict';
import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import ts from 'typescript';
import { generate, normalizeForNswag, validateReferences, command, findDotnet } from './generate.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const output = path.join(root, 'artifacts/v2/generated');
let passed = 0;
function check(name, assertion) { assertion(); passed++; console.log(`通过：${name}`); }
async function main() {
  const compiler = ts.createProgram({ rootNames: [path.join(output, 'web/client.ts'), path.join(output, 'web/schema.d.ts'), path.join(directory, 'Tests/type-contracts.ts')], options: {
    target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, moduleResolution: ts.ModuleResolutionKind.Bundler,
    strict: true, noEmit: true, skipLibCheck: false, lib: ['lib.es2022.d.ts', 'lib.dom.d.ts', 'lib.dom.iterable.d.ts'],
    paths: { 'openapi-fetch': [path.join(directory, 'node_modules/openapi-fetch/dist/index.d.mts')] },
  } });
  const errors = ts.getPreEmitDiagnostics(compiler);
  if (errors.length) throw new Error(ts.formatDiagnosticsWithColorAndContext(errors, { getCurrentDirectory: () => root, getCanonicalFileName: value => value, getNewLine: () => '\n' }));
  check('Web 类型检查、可空窗口、上传文件、安装包枚举和非法调用拒绝', () => assert.equal(errors.length, 0));
  const source = JSON.parse(await fs.readFile(path.join(output, 'openapi.source.json'), 'utf8'));
  const sdk = JSON.parse(await fs.readFile(path.join(output, 'openapi.sdk.json'), 'utf8'));
  check('生成文档全部本地引用均可解析', () => validateReferences(sdk));
  check('3.1 可空数字联合转换保留 C# 可空及数字字符串读取语义', () => {
    const transformed = normalizeForNswag({ openapi: '3.1.1', components: { schemas: { Number: { type: ['null', 'integer', 'string'], format: 'int64', pattern: '^-?[0-9]+$' } } } });
    assert.equal(transformed.components.schemas.Number.type, 'integer'); assert.equal(transformed.components.schemas.Number.nullable, true);
    assert.equal(transformed.components.schemas.Number['x-dotnet-number-string'], true);
  });
  check('无法处理的联合类型明确失败', () => assert.throws(() => normalizeForNswag({ openapi: '3.1.1', components: { schemas: { Mixed: { type: ['boolean', 'object'] } } } }), /不支持/));
  check('拒绝未固定版本的外部引用', () => assert.throws(() => validateReferences({ components: { schemas: { Remote: { $ref: 'https://invalid.test/schema.json' } } } }), /外部/));
  await fs.mkdir(path.join(directory, '.runtime'), { recursive: true });
  const runtime = ts.transpileModule(await fs.readFile(path.join(output, 'web/client.ts'), 'utf8'), { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
  const runtimePath = path.join(directory, '.runtime/web-runtime.mjs');
  await fs.writeFile(runtimePath, runtime);
  const { createVideoPlatformClient, uploadRelease } = await import(pathToFileURL(runtimePath));
  const seen = [];
  let identity = 'anonymous'; let csrfCount = 0; let failWrite = false;
  const fetcher = async request => {
    if (request.signal.aborted) throw new DOMException('请求已取消', 'AbortError');
    const route = new URL(request.url).pathname;
    seen.push({ route, method: request.method, csrf: request.headers.get('X-CSRF-Token'), credentials: request.credentials });
    if (route === '/api/v2/auth/csrf') { csrfCount++; await new Promise(resolve => setTimeout(resolve, 1)); return Response.json({ token: `csrf-${identity}` }); }
    if (['POST', 'PUT', 'DELETE'].includes(request.method)) assert.equal(request.headers.get('X-CSRF-Token'), `csrf-${identity}`);
    if (route === '/api/v2/auth/login') { identity = 'user'; return Response.json({ user: {}, expiresAt: '2026-09-07T12:00:00Z' }); }
    if (route === '/api/v2/auth/logout') { identity = 'anonymous'; return new Response(null, { status: 204 }); }
    if (route === '/api/v2/releases' && request.method === 'POST') {
      assert.match(request.headers.get('content-type'), /^multipart\/form-data; boundary=/);
      const form = await request.formData(); assert.equal(form.get('version'), '2.0.0'); assert.equal(form.get('file').name, 'test.msi');
      return Response.json({ id: 1 }, { status: 201 });
    }
    if (route.endsWith('/download')) return new Response(new Uint8Array([1, 2, 3]), { headers: { 'Content-Type': 'application/octet-stream' } });
    if (failWrite) { failWrite = false; return Response.json({ code: 'auth.csrf', message: '验证过期', traceId: 'sdk' }, { status: 400 }); }
    return Response.json([]);
  };
  const { client } = createVideoPlatformClient({ baseUrl: 'https://platform.test', fetch: fetcher });
  await client.POST('/api/v2/auth/login', { body: { username: 'test', password: 'test-password', clientType: 'web' } });
  check('Web 登录前取得匿名 CSRF，登录后重取身份对应令牌', () => { assert.equal(csrfCount, 2); assert.equal(seen[0].route, '/api/v2/auth/csrf'); assert.equal(seen[1].csrf, 'csrf-anonymous'); });
  await client.PUT('/api/v2/favorites', { body: { channelIds: [1] } });
  check('登录后的写请求使用新令牌且始终携带 Cookie', () => { assert.equal(seen.at(-1).csrf, 'csrf-user'); assert.ok(seen.every(request => request.credentials === 'include')); });
  await client.POST('/api/v2/auth/logout');
  check('退出后重取匿名 CSRF', () => assert.equal(csrfCount, 3));
  failWrite = true;
  const writesBefore = seen.filter(r => r.method === 'PUT').length;
  const failed = await client.PUT('/api/v2/favorites', { body: { channelIds: [1] } });
  check('失败写请求不会被自动重放且保留结构化错误', () => { assert.equal(failed.error.code, 'auth.csrf'); assert.equal(seen.filter(r => r.method === 'PUT').length, writesBefore + 1); });
  const csrfBefore = csrfCount;
  await Promise.all([1, 2, 3].map(id => client.PUT('/api/v2/favorites', { body: { channelIds: [id] } })));
  check('并发写入复用一次 CSRF 获取', () => assert.equal(csrfCount, csrfBefore + 1));
  await uploadRelease(client, { file: new Blob(['bytes']), fileName: 'test.msi', version: '2.0.0' });
  check('上传发送真实 FormData 与文件名', () => assert.equal(seen.at(-1).route, '/api/v2/releases'));
  const download = await client.GET('/api/v2/releases/{id}/download', { params: { path: { id: 5 } }, parseAs: 'blob' });
  check('下载返回实际 Blob 字节', () => assert.equal(download.data.size, 3));
  const controller = new AbortController(); controller.abort();
  await assert.rejects(client.GET('/api/v2/auth/me', { signal: controller.signal }), { name: 'AbortError' });
  check('Web 取消信号传递到 fetch', () => assert.ok(controller.signal.aborted));
  const snapshot = path.join(directory, '.runtime/repro-source.json'); await fs.writeFile(snapshot, JSON.stringify(source));
  const first = await generate({ input: snapshot, output: path.join(directory, '.runtime/repro-a'), strict: true });
  const second = await generate({ input: snapshot, output: path.join(directory, '.runtime/repro-b'), strict: true });
  check('相同 OpenAPI 快照重复生成全部文件哈希一致', () => assert.deepEqual(first.manifest.files, second.manifest.files));
  const dotnet = await findDotnet();
  const outputText = await command(dotnet, ['run', '--project', path.join(directory, 'Tests/VideoPlatform.ClientGenerator.Tests.csproj'), '--no-restore']);
  console.log(outputText.trim());
  console.log(`Web 与可重复生成验证通过：${passed} 项。`);
}
try { await main(); } catch (error) { console.error(`验证失败：${error.stack ?? error}`); process.exitCode = 1; }
