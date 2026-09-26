'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let prefs={...JotDesign.defaults};
function error(e){JotBridge.reportError(e,'settings');$('homeError').textContent=t(e.message);$('homeError').hidden=false;}
function render(){
  prefs=JotDesign.apply(prefs);
  for(const [attribute,key] of [['data-theme-choice','theme'],['data-weight','iconWeight']])
    document.querySelectorAll('['+attribute+']').forEach(button=>button.setAttribute('aria-pressed',String(button.getAttribute(attribute)===String(prefs[key]))));
  $('toolbarVisible').setAttribute('aria-checked',String(prefs.toolbarVisible!==false));$('homeFontSize').textContent=prefs.fontSize;
  $('homeLineHeight').textContent=prefs.lineHeight+'×';
  $('homeTighterLines').disabled=prefs.lineHeight<=1.2;$('homeLooserLines').disabled=prefs.lineHeight>=2.5;
  $('homeTighterLines').title=prefs.lineHeight<=1.2?'Minimum line height (1.2×)':'Decrease line height';
  $('homeLooserLines').title=prefs.lineHeight>=2.5?'Maximum line height (2.5×)':'Increase line height';
  $('homeStatus').textContent=t('ذخیره محلی · روی همین دستگاه');
}
async function preference(patch){
  prefs={...prefs,...patch};render();$('homeStatus').textContent=t('در حال ذخیره…');
  try{prefs=await request('preferences',patch);render();$('homeStatus').textContent=t('ذخیره شد');}
  catch(e){error(e);const data=await request('load');prefs=data.prefs;render();}
}
$('settingsBack').onclick=()=>request('home').catch(error);
$('trayVisibility').onclick=()=>request('tray-visibility').catch(error);
$('settingsQuit').onclick=async()=>{
  if($('settingsQuit').disabled)return;
  $('settingsQuit').disabled=true;$('settingsQuit').setAttribute('aria-busy','true');$('homeError').hidden=true;
  try{await request('quit');}
  catch(e){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(e);}
};
$('toolbarVisible').onclick=()=>preference({toolbarVisible:prefs.toolbarVisible===false});
$('homeTighterLines').onclick=()=>preference({lineHeight:JotDesign.stepLineHeight(prefs.lineHeight,-1)});
$('homeLooserLines').onclick=()=>preference({lineHeight:JotDesign.stepLineHeight(prefs.lineHeight,1)});
for(const [attribute,key] of [['data-theme-choice','theme'],['data-weight','iconWeight']])
  document.querySelectorAll('['+attribute+']').forEach(button=>button.onclick=()=>preference({[key]:key==='iconWeight'?Number(button.getAttribute(attribute)):button.getAttribute(attribute)}));
$('homeSmaller').onclick=()=>preference({fontSize:Math.max(13,prefs.fontSize-1)});$('homeLarger').onclick=()=>preference({fontSize:Math.min(24,prefs.fontSize+1)});
JotBridge.on(data=>{if(data.event==='preferences'){prefs=data.prefs;render();}if(data.event==='warning')error(new Error(data.message));if(data.event==='quit-failed'){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(new Error(data.message));}});
document.addEventListener('keydown',event=>{if(event.key==='Escape')request('home').catch(error);});
(async()=>{
  JotDesign.icons();JotDesign.drag($('settingsHandle'));
  const data=await request('preferences-load');prefs={...JotDesign.defaults,...data};render();window.jotReady=true;
})().catch(error);
