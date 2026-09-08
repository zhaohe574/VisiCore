export function readCatalog(catalog) {
  const responses = {}; const emptyResponses = new Set(); const binaryResponses = {};
  for (const [id, value] of Object.entries(catalog.responses)) {
    if (value.schemaName) responses[id] = [value.status, value.schemaName];
    else if (value.contentTypes) binaryResponses[id] = value.contentTypes;
    else emptyResponses.add(id);
  }
  const queryParameters = Object.fromEntries(Object.entries(catalog.queries).map(([id, queries]) => [id, queries.map(value => {
    const schema = { type: value.type };
    for (const key of ['format', 'default', 'minimum', 'maximum']) if (value[key] !== null) schema[key] = value[key];
    if (value.values) schema.enum = value.values;
    return { name: value.name, schema };
  })]));
  return { responses, emptyResponses, binaryResponses, queryParameters, publicOperations: new Set(catalog.publicOperations) };
}
export const uploadSchema = {
  type: 'object', required: ['file', 'version'], properties: {
    file: { type: 'string', format: 'binary' }, version: { type: 'string', maxLength: 32 },
    releaseNotes: { type: 'string', maxLength: 4096, default: '' }, minimumVersion: { type: 'string', maxLength: 32 },
    forceUpdate: { type: 'boolean', default: false },
  },
};
