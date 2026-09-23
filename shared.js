'use strict';
window.JotBridge = (() => {
  let sequence = 0;
  const jobs = new Map(), listeners = new Set();
  const host = window.chrome?.webview;
  host?.addEventListener('message', ({data}) => {
    if (data.event) { for (const listener of listeners) listener(data); return; }
    const job = jobs.get(data.id);
    if (!job) return;
    clearTimeout(job.timer); jobs.delete(data.id);
    data.ok ? job.resolve(data.value) : job.reject(Object.assign(new Error(data.error || 'عملیات انجام نشد.'),{
      operation:data.operation||job.action,code:data.code||'operation-failed',logged:!!data.logged
    }));
    if(data.ok&&job.action.startsWith('clipboard-'))for(const listener of listeners)listener({event:'clipboard-success'});
  });
  return {
    on: (listener) => listeners.add(listener),
    send: (action,payload=null) => host?.postMessage({action,payload}),
    reportError: (error,operation='renderer',details={}) => {
      if(error?.logged)return;
      const safe={operation,name:error?.name||'Error',code:error?.code||'unknown',
        source:(details.source||location.pathname).split(/[\\/]/).pop(),line:details.line||0,column:details.column||0};
      host?.postMessage({action:'log-error',payload:safe});
      if(error&&typeof error==='object')error.logged=true;
    },
    request: (action,payload=null) => new Promise((resolve,reject) => {
      if (!host) { reject(Object.assign(new Error('ارتباط با برنامه برقرار نیست.'),{operation:action})); return; }
      const id = ++sequence;
      const timer = setTimeout(() => { jobs.delete(id); const error=Object.assign(new Error('پاسخی از برنامه دریافت نشد.'),{operation:action,code:'bridge-timeout'});window.JotBridge.reportError(error,action);reject(error); },20000);
      jobs.set(id,{resolve,reject,timer,action}); host.postMessage({id,action,payload});
    })
  };
})();
window.addEventListener('error',event=>JotBridge.reportError(event.error||new Error(),'uncaught',{source:event.filename,line:event.lineno,column:event.colno}));
window.addEventListener('unhandledrejection',event=>JotBridge.reportError(event.reason,'unhandled-rejection'));
window.JotDesign = {
  nativeThemeKey: '',
  hexColor(value) {
    const canvas=document.createElement('canvas');canvas.width=canvas.height=1;
    const context=canvas.getContext('2d',{willReadFrequently:true});context.fillStyle=value;context.fillRect(0,0,1,1);
    return '#'+[...context.getImageData(0,0,1,1).data].slice(0,3).map(n=>n.toString(16).padStart(2,'0')).join('');
  },
  defaults: {theme:'dark',fontSize:16,accent:'neutral',iconWeight:1.8,coloredIcons:false,toolbarVisible:true,language:'en'},
  apply(prefs) {
    const p = {...this.defaults,...prefs};
    p.language='en';p.accent='neutral';p.coloredIcons=false;JotI18n.apply('en');
    const root = document.documentElement;
    const accent = window.JotAccents.find(item=>item.slug===p.accent) || window.JotAccents[0];
    const mode = p.theme==='light' ? 'light' : 'dark';
    root.dataset.theme = mode; root.dataset.color = accent.slug;
    root.dataset.coloredIcons = String(!!p.coloredIcons);
    root.style.setProperty('--primary',accent[mode].primary);
    root.style.setProperty('--primary-foreground',accent[mode].foreground);
    root.style.setProperty('--ring','color-mix(in oklab, var(--primary) 68%, var(--foreground))');
    root.style.setProperty('--icon-weight',Math.min(2.6,Math.max(1.3,Number(p.iconWeight)||1.8)));
    root.style.setProperty('--editor-size',Math.min(24,Math.max(13,Number(p.fontSize)||16))+'px');
    const native={mode,background:this.hexColor(accent[mode].primary),foreground:this.hexColor(accent[mode].foreground),weight:Number(p.iconWeight)||1.8,language:JotI18n.language};
    const key=JSON.stringify(native);
    if(key!==this.nativeThemeKey){this.nativeThemeKey=key;JotBridge.request('theme',native).catch(()=>{this.nativeThemeKey='';});}
    return p;
  },
  noteColor(slug) {
    const accent=JotAccents.find(item=>item.slug===slug)||JotAccents.find(item=>item.slug==='neutral');
    const mode=document.documentElement.dataset.theme==='light'?'light':'dark';
    return {...accent[mode],slug:accent.slug};
  },
  icon(name) {
    const element = document.createElementNS('http://www.w3.org/2000/svg','svg');
    element.setAttribute('viewBox','0 0 24 24');
    element.setAttribute('fill','none'); element.setAttribute('stroke','currentColor');
    element.setAttribute('stroke-linecap','round'); element.setAttribute('stroke-linejoin','round');
    element.setAttribute('aria-hidden','true'); element.classList.add('lucide');
    if(name==='jot')element.innerHTML='<path fill="currentColor" stroke="none" fill-rule="evenodd" d="M5 2h8v6a2 2 0 0 0 2 2h6v10a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2ZM7 12v2h10v-2Zm0 4v2h7v-2ZM15 2l6 6h-6Z"/>';
    else if(name==='trash-2')element.innerHTML='<path d="M3 6h18M9 6V4h6v2M5 6l1 14h12l1-14M10 10v6M14 10v6"/>';
    else element.innerHTML = window.JotIconPaths[name] || window.JotIconPaths['notepad-text'];
    return element;
  },
  icons(root=document) {
    root.querySelectorAll('[data-icon]').forEach(node=>{
      if (!node.querySelector('svg')) node.append(this.icon(node.dataset.icon));
    });
  },
  drag(element) {
    element.addEventListener('pointerdown',event=>{
      if(event.button===0&&!event.target.closest('button,input,select,a')) JotBridge.request('drag').catch(()=>{});
    });
  }
};
