'use strict';
(()=>{
const embedded=!!window.JotWorkspace;
const $=id=>document.getElementById(id),request=JotBridge.request,t=value=>JotI18n.text(value);
let prefs={...JotDesign.defaults},settingsBusy=false,shortcutsVisible=false,shortcutsMount;
let fullscreen=false,workspaceTabs=[],tabBusy=false,tabPendingId=null,tabPendingAction=null;
function renderWorkspaceTabs(){if(!embedded)JotWorkspaceTabs.render($('settingsTabs'),{tabs:workspaceTabs,activeId:'home',busy:tabBusy,pendingId:tabPendingId,pendingAction:tabPendingAction,onAction:workspaceAction});}
async function workspaceAction(action,id){
  if(tabBusy)return;tabBusy=true;tabPendingId=id;tabPendingAction=action;renderWorkspaceTabs();
  try{await request(action,id);}catch(e){error(e);}finally{tabBusy=false;tabPendingId=null;tabPendingAction=null;renderWorkspaceTabs();}
}
function fullscreenState(value){fullscreen=!!value;if(embedded)return;const b=$('settingsFullscreen');b.ariaPressed=String(fullscreen);b.title=b.ariaLabel=fullscreen?'Restore':'Maximize';b.replaceChildren(JotDesign.icon(fullscreen?'window-restore':'window-maximize'));}
if(!embedded)$('settingsFullscreen').onclick=async()=>{const b=$('settingsFullscreen');b.disabled=true;try{fullscreenState(await request('window-fullscreen',!fullscreen));}catch(e){error(e);}finally{b.disabled=false;}};
JotBridge.on(data=>{if(data.event==='window-fullscreen')fullscreenState(data.enabled);if(data.event==='tabs-changed'){workspaceTabs=data.tabs||[];renderWorkspaceTabs();}});
function error(e){JotBridge.reportError(e,'settings');const target=$(embedded?'settingsError':'homeError');target.textContent=t(e.message);target.hidden=true;JotToast.error(target.textContent,{id:'settings-error'});}
function render(){
  if(!embedded||JotWorkspace.view==='settings')prefs=JotDesign.apply(prefs);
  renderWorkspaceTabs();
  document.querySelectorAll('[data-theme-choice]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.themeChoice===prefs.theme)));
  document.querySelectorAll('[data-new-note-target]').forEach(button=>button.setAttribute('aria-pressed',String(button.dataset.newNoteTarget===(prefs.newNoteTarget||'tab'))));
  $('autoSaveFiles').setAttribute('aria-checked',String(!!prefs.autoSaveFiles));
  JotShortcutsPage.render(prefs);
  $('settingsFontSize').textContent=prefs.fontSize+' px';
  $('settingsFontSmaller').disabled=settingsBusy||prefs.fontSize<=13;$('settingsFontLarger').disabled=settingsBusy||prefs.fontSize>=24;
  const height=$('settingsLineHeight'),value=String(prefs.lineHeight);
  for(const option of height.querySelectorAll('[data-custom]'))if(option.value!==value)option.remove();
  if(![...height.options].some(option=>option.value===value)){const option=new Option(value+'×',value);option.dataset.custom='true';height.append(option);}
  height.value=value;height.disabled=settingsBusy;
}
async function preference(patch){
  if(settingsBusy)return;settingsBusy=true;$(embedded?'settingsError':'homeError').hidden=true;JotToast.dismiss('settings-error');
  if(embedded&&patch.newNoteTarget)JotWorkspace.setOpeningModeBusy(true);
  const buttons=[...document.querySelectorAll('[data-theme-choice],[data-new-note-target],#settingsFontSmaller,#settingsFontLarger,#settingsLineHeight,#autoSaveFiles,#globalShortcuts')];
  const toggle=patch.autoSaveFiles!==undefined?$('autoSaveFiles'):patch.globalShortcuts!==undefined?$('globalShortcuts'):null;
  const pendingButton=buttons.find(button=>patch.theme?button.dataset.themeChoice===patch.theme:patch.newNoteTarget&&button.dataset.newNoteTarget===patch.newNoteTarget);
  const label=pendingButton?.textContent;
  buttons.forEach(button=>button.disabled=true);
  if(toggle)toggle.ariaBusy='true';
  if(pendingButton){pendingButton.ariaBusy='true';pendingButton.textContent='Applying…';}
  $('homeStatus').textContent=patch.theme?'Applying theme…':patch.newNoteTarget?'Moving open notes…':toggle?'Applying setting…':'Applying writing defaults…';$('homeStatus').ariaBusy='true';
  try{prefs=await request('preferences',patch);render();$('homeStatus').textContent='';}
  catch(e){error(e);$('homeStatus').textContent='';}
  finally{settingsBusy=false;if(embedded&&patch.newNoteTarget)JotWorkspace.setOpeningModeBusy(false);$('homeStatus').ariaBusy='false';buttons.forEach(button=>button.disabled=false);if(toggle)toggle.removeAttribute('aria-busy');if(pendingButton){pendingButton.removeAttribute('aria-busy');pendingButton.textContent=label;}render();}
}
function showMain(){shortcutsVisible=false;JotShortcutsPage.hide();$('appearance').hidden=false;$('settingsPageTitle').textContent='Settings';$('settingsBack').title=$('settingsBack').ariaLabel='Back to Home';}
async function showShortcuts(){
  const button=$('settingsShortcuts');button.disabled=true;button.ariaBusy='true';
  try{shortcutsMount??=JotShortcutsPage.mount($('appearance').parentElement,preference);await shortcutsMount;JotShortcutsPage.render(prefs);shortcutsVisible=true;$('appearance').hidden=true;$('settingsPageTitle').textContent='Keyboard shortcuts';$('settingsBack').title=$('settingsBack').ariaLabel='Back to Settings';JotShortcutsPage.show();}
  catch(e){shortcutsMount=null;error(e);}finally{button.disabled=false;button.removeAttribute('aria-busy');}
}
$('settingsBack').onclick=()=>{if(shortcutsVisible){showMain();$('settingsShortcuts').focus({preventScroll:true});}else request('home').catch(error);};
$('settingsShortcuts').onclick=showShortcuts;
$('autoSaveFiles').onclick=()=>preference({autoSaveFiles:!prefs.autoSaveFiles});
if(!embedded){$('settingsClose').onclick=()=>request('hide').catch(error);$('settingsMinimize').onclick=()=>request('minimize').catch(error);}
$('settingsQuit').onclick=async()=>{
  if($('settingsQuit').disabled)return;
  $('settingsQuit').disabled=true;$('settingsQuit').setAttribute('aria-busy','true');$(embedded?'settingsError':'homeError').hidden=true;
  try{await request('quit');}
  catch(e){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');error(e);}
};
document.querySelectorAll('[data-theme-choice]').forEach(button=>button.onclick=()=>preference({theme:button.dataset.themeChoice}));
document.querySelectorAll('[data-new-note-target]').forEach(button=>button.onclick=()=>preference({newNoteTarget:button.dataset.newNoteTarget}));
$('settingsFontSmaller').onclick=()=>preference({fontSize:Math.max(13,prefs.fontSize-1)});
$('settingsFontLarger').onclick=()=>preference({fontSize:Math.min(24,prefs.fontSize+1)});
$('settingsLineHeight').onchange=()=>preference({lineHeight:Number($('settingsLineHeight').value)});
JotBridge.on(data=>{if(data.event==='preferences'){prefs=data.prefs;render();}if(data.event==='shortcuts-open')void showShortcuts();if(data.event==='warning'&&!embedded)error(new Error(data.message));if(data.event==='quit-failed'){$('settingsQuit').disabled=false;$('settingsQuit').setAttribute('aria-busy','false');if(!embedded)error(new Error(data.message));}});
window.JotSettings={preference,showShortcuts,showMain,get page(){return shortcutsVisible?'shortcuts':'settings';},show(data){if(data)prefs={...JotDesign.defaults,...data};render();}};
Object.defineProperty(window,'prefs',{configurable:true,get:()=>prefs});
JotSettings.ready=(async()=>{
  JotDesign.icons();if(!embedded)JotDesign.drag($('settingsHandle'));
  const [data,context]=await Promise.all([request('preferences-load'),request('context')]);prefs={...JotDesign.defaults,...data};fullscreenState(context.fullscreen);workspaceTabs=context.tabs||[];render();if(!embedded)window.jotReady=true;if(context.warning)error(Object.assign(new Error(context.warning),{logged:true}));
})();
if(!embedded)JotSettings.ready.catch(error);
})();
