(() => {
  if (window.__aivhDolaCaptureInstalled) return;
  window.__aivhDolaCaptureInstalled = true;

  const bridge = window.chrome?.webview;
  if (!bridge) return;

  const safe = (v) => { try { return JSON.parse(JSON.stringify(v)); } catch { return null; } };
  const post = (type, data = {}) => { try { bridge.postMessage({ type, source: 'dola-page', ...data }); } catch {} };
  const state = window.__aivhDolaState = window.__aivhDolaState || { resolving: {}, resolved: {}, query: '' };

  function findVid(node, path = '$', depth = 0) {
    if (depth > 9 || node == null) return [];
    const out = [];
    if (typeof node === 'string') {
      const s = node.trim();
      if (/^v[a-z0-9_-]{12,}$/i.test(s)) out.push({ vid: s, path });
      if ((s.startsWith('{') || s.startsWith('[')) && s.length < 500000) { try { out.push(...findVid(JSON.parse(s), path + '.__json', depth + 1)); } catch {} }
      return out;
    }
    if (Array.isArray(node)) { node.forEach((x,i) => out.push(...findVid(x, `${path}[${i}]`, depth + 1))); return out; }
    if (typeof node !== 'object') return out;
    for (const [k,v] of Object.entries(node)) {
      const p = `${path}.${k}`;
      if (typeof v === 'string' && /(^vid$|video[_-]?id|video[_-]?key|video[_-]?vid)/i.test(k) && /^v[a-z0-9_-]{8,}$/i.test(v)) out.push({ vid:v, path:p });
      out.push(...findVid(v, p, depth + 1));
    }
    return out;
  }

  async function resolveVid(vid, query = '') {
    if (!vid || state.resolving[vid] || state.resolved[vid]) return;
    state.resolving[vid] = true;
    post('dolaVid', { vid });
    const queries = [];
    if (query) queries.push(query);
    queries.push('aid=489823&device_platform=web&samantha_web=1&use-olympus-account=1&version_code=20800&pkg_type=release_version');
    queries.push('');
    let lastError = '';
    for (const q of queries) {
      try {
        const url = '/samantha/media/get_play_info' + (q ? ('?' + q) : '');
        const r = await fetch(url, { method:'POST', credentials:'include', headers:{'Content-Type':'application/json','Accept':'application/json, text/plain, */*'}, body:JSON.stringify({ key:vid, type:'video' }) });
        const text = await r.text();
        let payload = null; try { payload = JSON.parse(text); } catch { payload = { __raw:text }; }
        post('dolaPlayInfo', { vid, url, httpStatus:r.status, payload:safe(payload) });
        if (r.ok && payload && typeof payload === 'object') { state.resolved[vid] = true; break; }
        lastError = `HTTP ${r.status}`;
      } catch (e) { lastError = String(e && e.message || e); }
    }
    if (!state.resolved[vid]) post('status', { message:`Dola get_play_info 未完成：${lastError || '未知原因'}` });
    delete state.resolving[vid];
  }

  function inspectPayload(payload, url = '') {
    if (!payload || typeof payload !== 'object') return;
    const found = findVid(payload);
    for (const x of found.slice(0, 8)) resolveVid(x.vid, state.query);
    if (found.length) post('dolaVidObserved', { count:found.length, vids:found.slice(0,8).map(x => ({vid:x.vid,path:x.path})), url });
  }

  function recordQuery(url) {
    try { const u = new URL(url, location.href); if (u.origin === location.origin && (u.pathname.includes('chain/single') || u.pathname.includes('completion'))) state.query = u.search.replace(/^\?/, ''); } catch {}
  }

  const originalFetch = window.fetch;
  if (originalFetch) {
    window.fetch = async function(input, init) {
      const url = typeof input === 'string' ? input : (input && input.url) || '';
      recordQuery(url);
      const response = await originalFetch.apply(this, arguments);
      try {
        const clone = response.clone();
        const ct = clone.headers.get('content-type') || '';
        if (ct.includes('json') || String(url).includes('chain/single') || String(url).includes('completion')) { const t = await clone.text(); try { inspectPayload(JSON.parse(t), String(url)); } catch {} }
      } catch {}
      return response;
    };
  }

  const XO = XMLHttpRequest.prototype.open, XS = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.open = function(method, url) { this.__aivhDolaUrl = String(url || ''); recordQuery(this.__aivhDolaUrl); return XO.apply(this, arguments); };
  XMLHttpRequest.prototype.send = function() {
    this.addEventListener('loadend', () => { try { const url=this.__aivhDolaUrl||''; if ((this.getResponseHeader('content-type')||'').includes('json') || url.includes('chain/single') || url.includes('completion')) { const t=typeof this.responseText==='string'?this.responseText:''; if(t) inspectPayload(JSON.parse(t),url); } } catch {} });
    return XS.apply(this, arguments);
  };

  window.__aivhDolaResolveVideo = (vid) => resolveVid(String(vid || ''), state.query);
  window.__aivhDolaRescan = () => post('dolaRescan', { ok:true });
  post('dolaReady', { href:location.href });
})();
