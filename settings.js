'use strict';
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let prefs={...JotDesign.defaults},themeBusy=false;
function error(e){JotBridge.reportError(e,'settings');$('homeError').textContent=t(e.message);$('homeError').hidden=false;}
function render(){
  prefs=JotDesign.apply(prefs);
  document.querySelectorAll('[data-theme-choice]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.themeChoice===prefs.theme)));
}
async function preference(patch){
  if(themeBusy)return;themeBusy=true;$('homeError').hidden=true;
  document.querySelectorAll('[data-theme-choice]').forEach(button=>button.disabled=true);
  $('homeStatus').textContent='Applying theme…';$('homeStatus').ariaBusy='true';
  try{prefs=await request('preferences',patch);render();$('homeStatus').textContent='';}
  catch(e){error(e);$('homeStatus').textContent='';}
  finally{themeBusy=false;$('homeStatus').ariaBusy='false';document.querySelectorAll('[data-theme-choice]').forEach(button=>button.disabled=false);}
}
$('settingsBack').onclick=()=>request('home').catch(error);
$('settingsClose').onclick=()=>request('hide').catch(error);
$('settingsMinimize').onclick=()=>request('minimize').catch(error);
$('settingsQuit').onclick=async()=>{
  if($('settingsQuit').disabled)return;
  $('settingsQuit').disabled=true;$('settingsQuit').setAttribute('aria-busy','true');$('homeError').hidden=true;
  try{await request('quit');}
  catch(e){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(e);}
};
document.querySelectorAll('[data-theme-choice]').forEach(button=>button.onclick=()=>preference({theme:button.dataset.themeChoice}));
JotBridge.on(data=>{if(data.event==='preferences'){prefs=data.prefs;render();}if(data.event==='warning')error(new Error(data.message));if(data.event==='quit-failed'){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(new Error(data.message));}});
(async()=>{
  JotDesign.icons();JotDesign.drag($('settingsHandle'));
  const data=await request('preferences-load');prefs={...JotDesign.defaults,...data};render();window.jotReady=true;
})().catch(error);
