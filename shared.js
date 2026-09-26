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
  appliedPrefsKey: '',
  hexColor(value) {
    const canvas=document.createElement('canvas');canvas.width=canvas.height=1;
    const context=canvas.getContext('2d',{willReadFrequently:true});context.fillStyle=value;context.fillRect(0,0,1,1);
    return '#'+[...context.getImageData(0,0,1,1).data].slice(0,3).map(n=>n.toString(16).padStart(2,'0')).join('');
  },
  defaults: {theme:'dark',fontSize:16,lineHeight:1.95,accent:'neutral',iconWeight:1.8,coloredIcons:false,toolbarVisible:true,language:'en'},
  lineHeights: [1.2,1.5,1.75,1.95,2.2,2.5],
  stepLineHeight(value,step) {
    return step>0?(this.lineHeights.find(height=>height>value+.001)??2.5):([...this.lineHeights].reverse().find(height=>height<value-.001)??1.2);
  },
  apply(prefs) {
    const p = {...this.defaults,...prefs};
    p.language='en';p.accent='neutral';p.coloredIcons=false;
    p.lineHeight=Math.min(2.5,Math.max(1.2,Number(p.lineHeight)||1.95));
    const prefsKey=JSON.stringify(p);
    if(this.appliedPrefsKey===prefsKey&&this.nativeThemeKey)return p;
    this.appliedPrefsKey=prefsKey;JotI18n.apply('en');
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
    root.style.setProperty('--editor-line-height',p.lineHeight);
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
    if(name==='jot'){
      const mark=document.createElement('img');mark.src='assets/jot-color.png';mark.alt='';mark.className='app-mark';mark.setAttribute('aria-hidden','true');return mark;
    }
    const element = document.createElementNS('http://www.w3.org/2000/svg','svg');
    element.setAttribute('viewBox','0 0 24 24');
    element.setAttribute('fill','none'); element.setAttribute('stroke','currentColor');
    element.setAttribute('stroke-linecap','round'); element.setAttribute('stroke-linejoin','round');
    element.setAttribute('aria-hidden','true'); element.classList.add('lucide');
    if(name==='color-circle')element.innerHTML='<circle cx="12" cy="12" r="8" fill="currentColor" stroke="none"/>';
    else if(name==='line-height')element.innerHTML='<path d="M4 4v16m-3-3 3 3 3-3M1 7l3-3 3 3M11 5h11M11 12h11M11 19h11"/>';
    else if(name==='trash-2')element.innerHTML='<path d="M3 6h18M9 6V4h6v2M5 6l1 14h12l1-14M10 10v6M14 10v6"/>';
    else element.innerHTML = window.JotIconPaths[name] || window.JotIconPaths['notepad-text'];
    return element;
  },
  icons(root=document) {
    root.querySelectorAll('[data-icon]').forEach(node=>{
      if (!node.querySelector('svg,.app-mark')) node.append(this.icon(node.dataset.icon));
    });
  },
  drag(element) {
    element.addEventListener('pointerdown',event=>{
      if(event.button===0&&!event.target.closest('button,input,select,a')) JotBridge.request('drag').catch(()=>{});
    });
  }
};
