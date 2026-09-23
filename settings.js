'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let prefs={...JotDesign.defaults};
function error(e){JotBridge.reportError(e,'settings');$('homeError').textContent=t(e.message);$('homeError').hidden=false;}
function render(){
  prefs=JotDesign.apply(prefs);
  for(const [attribute,key] of [['data-theme-choice','theme'],['data-weight','iconWeight'],['data-accent','accent']])
    document.querySelectorAll('['+attribute+']').forEach(button=>button.setAttribute('aria-pressed',String(button.getAttribute(attribute)===String(prefs[key]))));
  $('coloredIcons').setAttribute('aria-checked',String(!!prefs.coloredIcons));$('toolbarVisible').setAttribute('aria-checked',String(prefs.toolbarVisible!==false));$('homeFontSize').textContent=prefs.fontSize;
  $('homeStatus').textContent=t('ذخیره محلی · روی همین دستگاه');
}
async function preference(patch){
  prefs={...prefs,...patch};render();$('homeStatus').textContent=t('در حال ذخیره…');
  try{prefs=await request('preferences',patch);render();$('homeStatus').textContent=t('ذخیره شد');}
  catch(e){error(e);const data=await request('load');prefs=data.prefs;render();}
}
$('settingsBack').onclick=()=>request('home').catch(error);
$('settingsQuit').onclick=async()=>{
  if($('settingsQuit').disabled)return;
  $('settingsQuit').disabled=true;$('settingsQuit').setAttribute('aria-busy','true');$('homeError').hidden=true;
  try{await request('quit');}
  catch(e){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(e);}
};
$('coloredIcons').onclick=()=>preference({coloredIcons:!prefs.coloredIcons});
$('toolbarVisible').onclick=()=>preference({toolbarVisible:prefs.toolbarVisible===false});
for(const [attribute,key] of [['data-theme-choice','theme'],['data-weight','iconWeight']])
  document.querySelectorAll('['+attribute+']').forEach(button=>button.onclick=()=>preference({[key]:key==='iconWeight'?Number(button.getAttribute(attribute)):button.getAttribute(attribute)}));
$('homeSmaller').onclick=()=>preference({fontSize:Math.max(13,prefs.fontSize-1)});$('homeLarger').onclick=()=>preference({fontSize:Math.min(24,prefs.fontSize+1)});
JotBridge.on(data=>{if(data.event==='preferences'){prefs=data.prefs;render();}if(data.event==='warning')error(new Error(data.message));if(data.event==='quit-failed'){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(new Error(data.message));}});
document.addEventListener('keydown',event=>{if(event.key==='Escape')request('home').catch(error);});
(async()=>{
  JotDesign.icons();JotDesign.drag($('settingsHandle'));
  for(const accent of JotAccents){
    const button=document.createElement('button');button.className='accent-choice';button.dataset.accent=accent.slug;button.title=accent.label;button.setAttribute('aria-label',accent.label);button.style.setProperty('--swatch',accent.swatch);button.append(JotDesign.icon('check'));
    button.onclick=()=>preference({accent:accent.slug});$('accentChoices').append(button);
  }
  const data=await request('load');prefs={...JotDesign.defaults,...data?.prefs};render();window.jotReady=true;
})().catch(error);
