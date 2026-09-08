import fs from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import openapiTS, { astToString } from 'openapi-typescript';
import ts from 'typescript';
import { readCatalog, uploadSchema } from './metadata.mjs';

const directory = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(directory, '../..');
const json = value => JSON.stringify(value, null, 2) + '\n';
const methods = new Set(['get', 'post', 'put', 'delete', 'patch', 'head', 'options']);
const hash = value => createHash('sha256').update(value).digest('hex');

export async function command(executable, args, options = {}) {
  return await new Promise((resolve, reject) => {
    const child = spawn(executable, args, { cwd: root, windowsHide: true, env: process.env, ...options });
    let stdout = ''; let stderr = '';
    child.stdout.on('data', data => { stdout += data; });
    child.stderr.on('data', data => { stderr += data; });
    child.on('error', reject);
    child.on('close', code => code === 0 ? resolve(stdout) : reject(new Error(`命令执行失败（${code}）：${executable}\n${stdout}\n${stderr}`)));
  });
}

export async function findDotnet() {
  if (process.env.DOTNET_EXE) return process.env.DOTNET_EXE;
  const bundled = path.join(process.env.LOCALAPPDATA ?? '', 'VideoPlatform/dotnet/dotnet.exe');
  if (process.platform === 'win32' && await exists(bundled)) return bundled;
  return 'dotnet';
}
async function exists(file) { try { await fs.access(file); return true; } catch { return false; } }

export function applyMetadata(source, catalog) {
  const { responses, emptyResponses, binaryResponses, queryParameters, publicOperations } = readCatalog(catalog);
  const schemas = catalog.schemas;
  const document = structuredClone(source);
  document.components ??= {};
  document.components.schemas ??= {};
  const notes = [];
  const ensureSchema = name => { document.components.schemas[name] ??= structuredClone(schemas[name]); };
  document.components.securitySchemes ??= {};
  document.components.securitySchemes.DesktopBearer ??= { type: 'http', scheme: 'bearer', description: '桌面登录令牌，由调用方安全存储。' };
  document.components.securitySchemes.WebCookie ??= { type: 'apiKey', in: 'cookie', name: '__Secure-VideoPlatform', description: '浏览器使用 HttpOnly 安全 Cookie，写入请求需 CSRF 令牌。' };
  const seen = new Set();
  for (const [route, item] of Object.entries(document.paths ?? {})) {
    if (!route.startsWith('/api/v2/') || route.startsWith('/api/v2/openapi/')) { delete document.paths[route]; continue; }
    for (const [method, operation] of Object.entries(item)) {
      if (!methods.has(method)) continue;
      const id = operation.operationId;
      if (!id || seen.has(id)) throw new Error(`operationId 缺失或重复：${method.toUpperCase()} ${route}`);
      seen.add(id);
      const note = (kind, detail) => notes.push({ operationId: id, method: method.toUpperCase(), path: route, kind, detail });
      operation.responses ??= {};
      const success = Object.entries(operation.responses).filter(([code]) => /^2\d\d$/.test(code));
      const structuredSuccess = success.some(([, response]) => Object.values(response.content ?? {}).some(content => content.schema));
      if (responses[id] && !structuredSuccess) {
        const [status, name] = responses[id];
        ensureSchema(name);
        for (const [code] of success) delete operation.responses[code];
        operation.responses[status] = { description: '成功响应', content: { 'application/json': { schema: { $ref: `#/components/schemas/${name}` } } } };
        note('response', `.Produces<${name}>(${status})`);
      }
      if (emptyResponses.has(id) && !operation.responses['204']) {
        if (structuredSuccess) throw new Error(`${id} 的响应已经变为有正文，请更新补充表后重新生成。`);
        for (const [code] of success) delete operation.responses[code];
        operation.responses['204'] = { description: '操作完成，无响应正文' };
        note('response', '.Produces(204)');
      }
      if (binaryResponses[id] && !structuredSuccess) {
        for (const [code] of success) delete operation.responses[code];
        operation.responses['200'] = { description: '文件内容', content: Object.fromEntries(binaryResponses[id].map(type => [type, { schema: { type: 'string', format: 'binary' } }])) };
        note('response', `.Produces(200, contentType: "${binaryResponses[id][0]}")，二进制 schema`);
      }
      if (id === 'GetLatestRelease' && !operation.responses['204']) {
        operation.responses['204'] = { description: '尚未发布客户端版本' };
        note('response', '最新发布接口补充 .Produces(204)');
      }
      if (id === 'UploadRelease' && !operation.requestBody) {
        operation.requestBody = { required: true, content: { 'multipart/form-data': { schema: structuredClone(uploadSchema) } } };
        note('request', '为 multipart/form-data 注册 file/version/releaseNotes/minimumVersion/forceUpdate 字段和文件类型。');
      }
      for (const parameter of queryParameters[id] ?? []) {
        operation.parameters ??= [];
        const existing = operation.parameters.find(p => p.in === 'query' && p.name === parameter.name);
        if (!existing) {
          operation.parameters.push({ in: 'query', required: false, ...structuredClone(parameter) });
          note('query', `显式声明查询参数 ${parameter.name}，当前从 HttpContext 读取，OpenAPI 无法自动发现。`);
        } else if (parameter.schema.enum && !existing.schema?.enum) {
          existing.schema = structuredClone(parameter.schema);
          note('query', `查询参数 ${parameter.name} 缺少允许值：${parameter.schema.enum.join('、')}。`);
        }
      }
      for (const parameter of operation.parameters ?? []) {
        if (parameter.in !== 'path' || parameter.name !== 'kind' || parameter.schema?.enum) continue;
        if (route.includes('/organization/')) parameter.schema = { type: 'string', enum: ['workshops', 'areas', 'units'] };
        if (route.includes('/scopes/')) parameter.schema = { type: 'string', enum: ['user', 'role'] };
      }
      if (!operation.security) operation.security = publicOperations.has(id) ? [] : [{ DesktopBearer: [] }, { WebCookie: [] }];
      for (const status of [400, 401, 403, 404, 409, 429, 500, 503]) {
        ensureSchema('ErrorResponse');
        operation.responses[status] ??= { description: '请求失败；认证中间件也可能返回空正文。', content: { 'application/json': { schema: { $ref: '#/components/schemas/ErrorResponse' } } } };
      }
      if (!Object.keys(operation.responses).some(code => /^2\d\d$/.test(code))) throw new Error(`${id} 没有成功响应定义。`);
      if (!responses[id] && !emptyResponses.has(id) && !binaryResponses[id] && !structuredSuccess)
        throw new Error(`${id} 缺少响应类型，无法可靠生成客户端。请先补充 API 元数据或审核补充表。`);
    }
  }
  validateReferences(document);
  return { document, notes, count: seen.size };
}

export function validateReferences(document) {
  function visit(node) {
    if (!node || typeof node !== 'object') return;
    if (typeof node.$ref === 'string') {
      if (!node.$ref.startsWith('#/')) throw new Error(`不允许未锁定的外部 schema 引用：${node.$ref}`);
      const target = node.$ref.slice(2).split('/').map(p => p.replaceAll('~1', '/').replaceAll('~0', '~')).reduce((value, key) => value?.[key], document);
      if (target === undefined) throw new Error(`schema 引用不存在：${node.$ref}`);
    }
    for (const value of Object.values(node)) visit(value);
  }
  visit(document);
}

export function normalizeForNswag(document) {
  const result = structuredClone(document);
  result.openapi = '3.0.3';
  delete result.jsonSchemaDialect;
  function visit(node, parent, key) {
    if (typeof node === 'boolean' && ['schema', 'items'].includes(key)) { parent[key] = node ? {} : { not: {} }; return; }
    if (!node || typeof node !== 'object') return;
    delete node.$schema;
    for (const property of Object.keys(node.properties ?? {})) {
      if (typeof node.properties[property] === 'boolean') node.properties[property] = node.properties[property] ? {} : { not: {} };
    }
    if (Array.isArray(node.type)) {
      const nonNull = node.type.filter(type => type !== 'null');
      if (node.type.includes('null')) node.nullable = true;
      if (nonNull.length === 1) node.type = nonNull[0];
      else if (nonNull.includes('string') && nonNull.some(type => ['integer', 'number'].includes(type)) && node.pattern) {
        // .NET 的数字字符串兼容输入在 C# 中映射为数字，客户端 JSON 设置保留字符串读取支持。
        node['x-dotnet-number-string'] = true;
        node.type = nonNull.includes('integer') ? 'integer' : 'number';
        delete node.pattern;
      } else throw new Error(`NSwag 不支持未经处理的联合类型：${JSON.stringify(node.type)}`);
    }
    if (node.type === 'null') { delete node.type; node.nullable = true; }
    for (const [childKey, value] of Object.entries(node)) visit(value, node, childKey);
  }
  visit(result);
  validateReferences(result);
  return result;
}

export async function generate(options = {}) {
  const output = path.resolve(options.output ?? path.join(root, 'artifacts/v2/generated'));
  const dotnet = await findDotnet();
  const sourceUrl = options.url ?? 'http://127.0.0.1:5082/api/v2/openapi/v2.json';
  let sourceText;
  if (options.input) sourceText = await fs.readFile(path.resolve(options.input), 'utf8');
  else {
    const response = await fetch(sourceUrl, { signal: AbortSignal.timeout(30000), redirect: 'error' });
    if (!response.ok) throw new Error(`读取 OpenAPI 失败：HTTP ${response.status}`);
    sourceText = await response.text();
  }
  if (Buffer.byteLength(sourceText) > 20 * 1024 * 1024) throw new Error('OpenAPI 文档超过 20 MB 限制。');
  const source = JSON.parse(sourceText);
  if (!/^3\.[01]\./.test(source.openapi ?? '')) throw new Error('只支持 OpenAPI 3.0 和 3.1 文档。');
  await command(dotnet, ['build', path.join(directory, 'VideoPlatform.ClientGenerator.csproj'), '--no-restore', '--verbosity', 'quiet']);
  const schemaText = await command(dotnet, [path.join(directory, 'bin/Debug/net10.0/VideoPlatform.ClientGenerator.dll')]);
  const { document, notes, count } = applyMetadata(source, JSON.parse(schemaText));
  if (options.strict && notes.length) throw new Error(`严格模式拒绝生成：API 仍缺少 ${notes.length} 项元数据。先执行普通模式查看报告。`);
  await fs.mkdir(path.join(output, 'web'), { recursive: true });
  await fs.mkdir(path.join(output, 'csharp'), { recursive: true });
  await fs.writeFile(path.join(output, 'openapi.source.json'), json(source));
  await fs.writeFile(path.join(output, 'openapi.sdk.json'), json(document));
  await fs.writeFile(path.join(output, 'openapi.nswag.json'), json(normalizeForNswag(document)));
  const ast = await openapiTS(document, { alphabetize: true, exportType: true, immutable: false,
    transform: schema => schema.format === 'binary' ? ts.factory.createTypeReferenceNode('Blob') : undefined });
  await fs.writeFile(path.join(output, 'web/schema.d.ts'), '// 此文件由 OpenAPI 自动生成，请运行客户端生成工具更新。\n' + astToString(ast));
  for (const name of ['client.ts', 'package.json', 'tsconfig.json'])
    await fs.copyFile(path.join(directory, 'templates/web', name), path.join(output, 'web', name));
  const config = {
    runtime: 'Net100', documentGenerator: { fromDocument: { url: 'openapi.nswag.json' } },
    codeGenerators: { openApiToCSharpClient: {
      className: 'VideoPlatformClient', namespace: 'VideoPlatform.Client.Generated', output: 'csharp/VideoPlatformClient.g.cs',
      generateClientInterfaces: true, injectHttpClient: true, disposeHttpClient: false, useBaseUrl: false,
      operationGenerationMode: 'SingleClientFromOperationId', generateOptionalParameters: true,
      jsonLibrary: 'SystemTextJson', generateNullableReferenceTypes: true, generateOptionalPropertiesAsNullable: true,
      dateType: 'System.DateTimeOffset', dateTimeType: 'System.DateTimeOffset', generateJsonMethods: false,
    } },
  };
  await fs.writeFile(path.join(output, 'nswag.json'), json(config));
  const nswag = path.join(directory, 'node_modules/nswag/bin/binaries/Net100/dotnet-nswag.dll');
  await command(dotnet, [nswag, 'run', 'nswag.json'], { cwd: output });
  const generated = path.join(output, 'csharp/VideoPlatformClient.g.cs');
  const generatedText = await fs.readFile(generated, 'utf8');
  // 只统一换行和行尾空白；模型和客户端代码完全由 NSwag 输出。
  await fs.writeFile(generated, '// 此文件由 NSwag 根据 OpenAPI 自动生成，请勿手动编辑。\n' + generatedText.replaceAll('\r\n', '\n').replace(/[ \t]+$/gm, ''));
  const report = ['# OpenAPI 元数据接入报告', '', `实际发现 ${count} 个业务操作，补充 ${notes.length} 项 API 元数据。`, '',
    '以下补充仅作用于 SDK 文档，未修改服务端。主代理应将这些定义移回端点注册，随后使用 --strict 验证不再依赖补充表。', '',
    '| 操作 | 方法与路径 | 缺失类型 | 建议 |', '| --- | --- | --- | --- |',
    ...notes.map(n => `| ${n.operationId} | ${n.method} ${n.path} | ${n.kind} | ${n.detail.replaceAll('|', '\\|')} |`), '',
    '分页别名 PagedDeviceResponse／PagedChannelResponse／PagedAlarmResponse／PagedExportResponse／PagedReleaseResponse 对应 Contracts.PagedResponse<T>；RecordingListResponse 对应 RecordingDto[]。', '',
    '所有业务接口还应统一声明 ErrorResponse 错误响应、Cookie／Bearer 认证，以及浏览器写入所需 X-CSRF-Token 请求头。', '',
    '源文档保持 3.1。NSwag 输入另存为 3.0.3，保留 nullable，并将 .NET 数字／数字字符串联合类型映射为 C# 数字；C# 客户端允许读取数字字符串。', '',
    '时间在 Web 中保持 ISO 8601 字符串，在 C# 中使用 DateTimeOffset。Web 中 int64 使用 number | string，调用方应保留超出安全整数范围的字符串。', '',
    '当前实际响应：CreateDevice 为 201 {id}，CreateExport 为 202 {id,state,progress}，RetryExport 为 202 {id,state}。这些类型没有伪装为完整业务对象。', ''];
  await fs.writeFile(path.join(output, 'metadata-report.md'), report.join('\n'));
  await fs.copyFile(path.join(directory, 'README.md'), path.join(output, 'README.md'));
  const files = ['openapi.source.json', 'openapi.sdk.json', 'openapi.nswag.json', 'nswag.json', 'metadata-report.md', 'README.md', 'web/schema.d.ts', 'web/client.ts', 'web/package.json', 'web/tsconfig.json', 'csharp/VideoPlatformClient.g.cs'];
  const fileHashes = {};
  for (const name of files) fileHashes[name] = hash(await fs.readFile(path.join(output, name)));
  const manifest = { generatorVersion: '2.0.0', source: options.input ? 'local-file' : sourceUrl, sourceSha256: hash(json(source)),
    operationCount: count, supplementCount: notes.length, tools: { nswag: '14.7.1', openapiTypescript: '7.13.0' }, files: fileHashes };
  await fs.writeFile(path.join(output, 'manifest.json'), json(manifest));
  console.log(`已生成 ${count} 个操作的 Web 契约和 C# 客户端：${output}`);
  console.log(`服务端元数据待补 ${notes.length} 项，详见 metadata-report.md。`);
  return { output, manifest, notes };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const args = process.argv.slice(2); const options = {};
    for (let i = 0; i < args.length; i++) {
      if (args[i] === '--strict') options.strict = true;
      else if (['--url', '--input', '--output'].includes(args[i]) && args[i + 1]) options[args[i].slice(2)] = args[++i];
      else throw new Error(`未知参数或缺少参数值：${args[i]}`);
    }
    await generate(options);
  } catch (error) { console.error(`生成失败：${error.message}`); process.exitCode = 1; }
}
