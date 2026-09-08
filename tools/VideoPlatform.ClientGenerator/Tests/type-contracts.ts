import { createVideoPlatformClient, uploadRelease, type components } from '../../../artifacts/v2/generated/web/client';

const { client } = createVideoPlatformClient({ baseUrl: 'https://platform.test' });
const layout: components['schemas']['LayoutRequest'] = { name: '四屏', kind: 'layout', shared: false, layout: 4, intervalSeconds: 30, channelIds: [null, 1, null, '9007199254740993'] };
client.POST('/api/v2/layouts', { body: layout });
client.GET('/api/v2/users', { params: { query: { page: 2, pageSize: 50, search: '值班' } } });
client.GET('/api/v2/public/releases/latest', { params: { query: { packageType: 'zip', currentVersion: '2.0.0' } } }).then(({ data }) => data?.packages.map(value => value.fileName));
client.GET('/api/v2/releases/{id}/download', { params: { path: { id: 1 } }, parseAs: 'blob' });
uploadRelease(client, { file: new Blob(['安装包']), fileName: 'client.msi', version: '2.0.0' });
// @ts-expect-error 包类型必须来自服务端声明的枚举。
client.GET('/api/v2/public/releases/latest', { params: { query: { packageType: 'exe' } } });
// @ts-expect-error 不允许调用 OpenAPI 中未声明的路径。
client.GET('/api/v2/not-a-real-route');
// @ts-expect-error 创建布局必须携带请求正文。
client.POST('/api/v2/layouts');
