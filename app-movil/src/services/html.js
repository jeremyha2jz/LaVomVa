const HTML_ENTITIES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

export function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, character => HTML_ENTITIES[character]);
}

export function safeClassName(value) {
  return String(value ?? '').toLowerCase().replace(/[^a-z0-9_-]/g, '');
}
