// 运行时模板随 OpenAPI 契约一同生成，路径和请求／响应类型来自 schema.d.ts。
import createClient, { type Middleware } from 'openapi-fetch';
import type { paths, components } from './schema';
export type { paths, components, operations } from './schema';

export interface VideoPlatformClientOptions {
  baseUrl: string;
  fetch?: typeof globalThis.fetch;
}

export function createVideoPlatformClient(options: VideoPlatformClientOptions) {
  const baseUrl = options.baseUrl.replace(/\/+$/, '');
  const fetcher = options.fetch ?? globalThis.fetch.bind(globalThis);
  let csrf: string | undefined;
  let pending: Promise<string> | undefined;
  let identity = 0;
  function invalidateCsrf() { csrf = undefined; pending = undefined; identity++; }
  async function csrfToken(): Promise<string> {
    if (csrf) return csrf;
    if (pending) return pending;
    const epoch = identity;
    const operation = (async () => {
      const response = await fetcher(new Request(`${baseUrl}/api/v2/auth/csrf`, { credentials: 'include' }));
      if (!response.ok) throw new Error(`获取请求验证令牌失败：HTTP ${response.status}`);
      const value = await response.json() as { token?: unknown };
      if (typeof value.token !== 'string' || !value.token) throw new Error('服务端未返回有效请求验证令牌。');
      if (epoch !== identity) return csrfToken();
      csrf = value.token;
      return csrf;
    })();
    pending = operation;
    try { return await operation; }
    finally { if (pending === operation) pending = undefined; }
  }
  const middleware: Middleware = {
    async onRequest({ request }) {
      if (['POST', 'PUT', 'PATCH', 'DELETE'].includes(request.method)) request.headers.set('X-CSRF-Token', await csrfToken());
      return request;
    },
    async onResponse({ request, response }) {
      const pathname = new URL(request.url).pathname;
      if (response.ok && ['/api/v2/auth/login', '/api/v2/auth/logout', '/api/v2/auth/password'].includes(pathname)) {
        invalidateCsrf();
        await csrfToken();
      } else if (response.status === 401) invalidateCsrf();
      else if (response.status === 400) {
        try { if ((await response.clone().json() as { code?: string }).code === 'auth.csrf') invalidateCsrf(); } catch { /* 非 JSON 错误保留给调用方处理。 */ }
      }
      return response;
    },
  };
  const client = createClient<paths>({ baseUrl, fetch: fetcher, credentials: 'include' });
  client.use(middleware);
  return { client, refreshCsrf: async () => { invalidateCsrf(); return csrfToken(); } };
}

export interface ReleaseUpload {
  file: Blob;
  fileName: string;
  version: string;
  releaseNotes?: string;
  minimumVersion?: string;
  forceUpdate?: boolean;
}

export function releaseForm(value: ReleaseUpload): FormData {
  const form = new FormData();
  form.set('file', value.file, value.fileName);
  form.set('version', value.version);
  form.set('releaseNotes', value.releaseNotes ?? '');
  if (value.minimumVersion) form.set('minimumVersion', value.minimumVersion);
  form.set('forceUpdate', String(value.forceUpdate ?? false));
  return form;
}

export type ApiError = components['schemas']['ErrorResponse'];

export function uploadRelease(client: ReturnType<typeof createVideoPlatformClient>['client'], value: ReleaseUpload, signal?: AbortSignal) {
  return client.POST('/api/v2/releases', {
    body: { file: value.file, version: value.version, releaseNotes: value.releaseNotes, minimumVersion: value.minimumVersion, forceUpdate: value.forceUpdate },
    bodySerializer: () => releaseForm(value), signal,
  });
}
